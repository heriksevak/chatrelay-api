// ============================================================
//  ChatRelay — AutoReplyEngine
//  Core feature: matches inbound messages against rules
//  and sends automatic replies via the message queue.
//
//  Match order:
//  1. Rules sorted by Priority (ascending — lower = higher priority)
//  2. Within same priority — more specific match type wins
//     (Exact > StartsWith > Contains > Regex)
//  3. First match wins — no multiple replies per message
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.Models;
using ChatRelay.API.Queue;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatRelay.API.Services;

public interface IAutoReplyEngine
{
    Task ProcessInboundAsync(Guid wabaId, Guid contactId,
        Guid conversationId, string inboundText);

    Task<(bool Matched, string? RuleName, string? ReplyPreview, string? Reason)>
        TestMatchAsync(Guid wabaId, string inboundText);
}

public class AutoReplyEngine : IAutoReplyEngine
{
    private readonly ApplicationDbContext _db;
    private readonly IMessageQueue _queue;
    private readonly ILogger<AutoReplyEngine> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization
            .JsonIgnoreCondition.WhenWritingNull
    };

    public AutoReplyEngine(
        ApplicationDbContext db,
        IMessageQueue queue,
        ILogger<AutoReplyEngine> logger)
    {
        _db     = db;
        _queue  = queue;
        _logger = logger;
    }

    // ── Main entry point — called by WebhookProcessor ─────────

    public async Task ProcessInboundAsync(
        Guid wabaId,
        Guid contactId,
        Guid conversationId,
        string inboundText)
    {
        if (string.IsNullOrWhiteSpace(inboundText)) return;

        // Load active rules for this WABA sorted by priority
        var rules = await _db.AutoReplyRules
            .Include(r => r.Template)
            .Where(r => r.WabaId == wabaId && r.IsActive)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();

        if (!rules.Any()) return;

        var normalized = inboundText.Trim();

        foreach (var rule in rules)
        {
            // Check schedule
            if (!IsWithinSchedule(rule)) continue;

            // Check throttle — don't spam the same contact
            if (await IsThrottledAsync(rule, contactId)) continue;

            // Try to match
            if (!IsMatch(rule, normalized)) continue;

            // Matched — send auto reply
            _logger.LogInformation(
                "AutoReply rule '{Rule}' matched for contact {ContactId} msg='{Msg}'",
                rule.RuleName, contactId, normalized[..Math.Min(normalized.Length, 50)]);

            await SendAutoReplyAsync(rule, wabaId, contactId, conversationId);

            // Update rule stats
            rule.TriggerCount++;
            rule.LastTriggeredAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // First match wins — stop checking other rules
            return;
        }
    }

    // ── Test match without sending ────────────────────────────

    public async Task<(bool Matched, string? RuleName, string? ReplyPreview, string? Reason)>
        TestMatchAsync(Guid wabaId, string inboundText)
    {
        var rules = await _db.AutoReplyRules
            .Include(r => r.Template)
            .Where(r => r.WabaId == wabaId && r.IsActive)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();

        if (!rules.Any())
            return (false, null, null, "No active rules for this WABA");

        var normalized = inboundText.Trim();

        foreach (var rule in rules)
        {
            if (!IsWithinSchedule(rule))
            {
                continue; // skip silently in test — schedule mismatch is not a hard fail
            }

            if (!IsMatch(rule, normalized)) continue;

            var preview = rule.ReplyType == ReplyType.Text
                ? rule.ReplyContent
                : $"[Template] {rule.Template?.Name ?? rule.TemplateId.ToString()}";

            return (true, rule.RuleName, preview, null);
        }

        return (false, null, null, "No rules matched the message");
    }

    // ── Matching logic ────────────────────────────────────────

    private static bool IsMatch(AutoReplyRule rule, string message)
    {
        return rule.TriggerType switch
        {
            TriggerType.AnyMessage   => true,
            TriggerType.FirstMessage => true,  // handled separately via contact stats
            TriggerType.Keyword      => MatchKeywords(rule, message),
            TriggerType.OutsideHours => !IsWithinSchedule(rule),
            _ => false
        };
    }

    private static bool MatchKeywords(AutoReplyRule rule, string message)
    {
        if (string.IsNullOrEmpty(rule.Keywords)) return false;

        List<string>? keywords;
        try
        {
            keywords = JsonSerializer.Deserialize<List<string>>(rule.Keywords);
        }
        catch
        {
            // Fallback: treat as comma-separated
            keywords = rule.Keywords
                .Split(',')
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
        }

        if (keywords == null || !keywords.Any()) return false;

        return rule.MatchType switch
        {
            ChatRelay.Models.MatchType.Exact      => keywords.Any(k =>
                string.Equals(message, k, StringComparison.OrdinalIgnoreCase)),

            ChatRelay.Models.MatchType.Contains   => keywords.Any(k =>
                message.Contains(k, StringComparison.OrdinalIgnoreCase)),

            ChatRelay.Models.MatchType.StartsWith => keywords.Any(k =>
                message.StartsWith(k, StringComparison.OrdinalIgnoreCase)),

            ChatRelay.Models.MatchType.Regex      => keywords.Any(k =>
            {
                try { return Regex.IsMatch(message, k, RegexOptions.IgnoreCase); }
                catch { return false; }
            }),

            _ => false
        };
    }

    // ── Schedule check ────────────────────────────────────────

    private static bool IsWithinSchedule(AutoReplyRule rule)
    {
        // No schedule = always active
        if (!rule.ActiveFromTime.HasValue && !rule.ActiveUntilTime.HasValue
            && !rule.ActiveDaysBitmask.HasValue)
            return true;

        // Resolve timezone
        TimeZoneInfo tz;
        try
        {
            tz = !string.IsNullOrEmpty(rule.ActiveTimezone)
                ? TimeZoneInfo.FindSystemTimeZoneById(rule.ActiveTimezone)
                : TimeZoneInfo.Utc;
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

        // Check day of week bitmask
        if (rule.ActiveDaysBitmask.HasValue)
        {
            var dayBit = (int)localNow.DayOfWeek switch
            {
                0 => 64,   // Sunday
                1 => 1,    // Monday
                2 => 2,    // Tuesday
                3 => 4,    // Wednesday
                4 => 8,    // Thursday
                5 => 16,   // Friday
                6 => 32,   // Saturday
                _ => 0
            };

            if ((rule.ActiveDaysBitmask.Value & dayBit) == 0)
                return false;
        }

        // Check time window
        if (rule.ActiveFromTime.HasValue && rule.ActiveUntilTime.HasValue)
        {
            var currentTime = TimeOnly.FromDateTime(localNow);
            var from        = rule.ActiveFromTime.Value;
            var until       = rule.ActiveUntilTime.Value;

            // Handle overnight windows (e.g. 22:00 - 06:00)
            if (from <= until)
                return currentTime >= from && currentTime <= until;
            else
                return currentTime >= from || currentTime <= until;
        }

        return true;
    }

    // ── Throttle check ────────────────────────────────────────

    private async Task<bool> IsThrottledAsync(AutoReplyRule rule, Guid contactId)
    {
        if (!rule.ThrottleMinutes.HasValue) return false;

        if (!rule.LastTriggeredAt.HasValue) return false;

        // Check if this specific rule fired for this contact recently
        var recentAutoReply = await _db.Messages
            .AnyAsync(m =>
                m.ContactId      == contactId &&
                m.AutoReplyRuleId == rule.Id &&
                m.IsAutomated    == true &&
                m.CreatedAt      >= DateTime.UtcNow
                    .AddMinutes(-rule.ThrottleMinutes.Value));

        return recentAutoReply;
    }

    // ── Send the auto reply ───────────────────────────────────

    private async Task SendAutoReplyAsync(
        AutoReplyRule rule,
        Guid wabaId,
        Guid contactId,
        Guid conversationId)
    {
        // Build message payload based on reply type
        string rawPayload;

        if (rule.ReplyType == ReplyType.Text)
        {
            var textPayload = new
            {
                type = "Text",
                text = new { body = rule.ReplyContent }
            };
            rawPayload = JsonSerializer.Serialize(textPayload, JsonOpts);
        }
        else // Template
        {
            // Build template params if present
            List<object>? bodyParams = null;
            if (!string.IsNullOrEmpty(rule.TemplateParams))
            {
                var rawParams = JsonSerializer
                    .Deserialize<List<string>>(rule.TemplateParams);
                bodyParams = rawParams?
                    .Select(p => (object)new { type = "text", text = p })
                    .ToList();
            }

            var templatePayload = new
            {
                type     = "Template",
                template = new
                {
                    name       = rule.Template?.Name ?? string.Empty,
                    language   = rule.Template?.Language ?? "en",
                    bodyParams
                }
            };
            rawPayload = JsonSerializer.Serialize(templatePayload, JsonOpts);
        }

        // Create message record directly (skip API layer)
        var message = new Message
        {
            Id              = Guid.NewGuid(),
            WabaId          = wabaId,
            ConversationId  = conversationId,
            ContactId       = contactId,
            Direction       = MessageDirection.Outbound,
            MessageType     = rule.ReplyType == ReplyType.Text
                                ? MessageType.Text
                                : MessageType.Template,
            Status          = MessageStatus.Queued,
            Content         = rule.ReplyType == ReplyType.Text
                                ? rule.ReplyContent
                                : $"[Template] {rule.Template?.Name}",
            TemplateId      = rule.TemplateId,
            IsAutomated     = true,
            AutoReplyRuleId = rule.Id,
            QueuedAt        = DateTime.UtcNow,
            RawPayload      = rawPayload,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow,
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        // Push to queue — MessageWorker picks it up and sends via AiSensy
        _queue.Enqueue(new QueuedMessage
        {
            MessageId = message.Id,
            WabaId    = wabaId,
        });

        _logger.LogInformation(
            "AutoReply message {Id} queued for contact {ContactId} rule='{Rule}'",
            message.Id, contactId, rule.RuleName);
    }
}
