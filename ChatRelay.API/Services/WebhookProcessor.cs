// ============================================================
//  ChatRelay — IWebhookProcessor + WebhookProcessor
//  Handles every Meta webhook event type
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.Models;
using ChatRelay.API.Webhooks;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChatRelay.API.Services;

public interface IWebhookProcessor
{
    Task ProcessAsync(MetaWebhookPayload payload);
}

public class WebhookProcessor : IWebhookProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<WebhookProcessor> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        WriteIndented          = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization
            .JsonIgnoreCondition.WhenWritingNull
    };

    public WebhookProcessor(ApplicationDbContext db, ILogger<WebhookProcessor> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Main dispatcher ───────────────────────────────────────

    public async Task ProcessAsync(MetaWebhookPayload payload)
    {
        if (payload.Object != "whatsapp_business_account")
        {
            _logger.LogWarning("Unknown webhook object type: {Type}", payload.Object);
            return;
        }

        foreach (var entry in payload.Entry)
        {
            foreach (var change in entry.Changes)
            {
                try
                {
                    await DispatchChangeAsync(entry.Id, change);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error processing webhook change field={Field} wabaId={WabaId}",
                        change.Field, entry.Id);
                    // Don't throw — Meta retries if we return non-200
                    // Log and continue processing other changes
                }
            }
        }
    }

    // ── Route by field ────────────────────────────────────────

    private async Task DispatchChangeAsync(string metaWabaId, MetaWebhookChange change)
    {
        _logger.LogDebug("Webhook field={Field} wabaId={WabaId}", change.Field, metaWabaId);

        switch (change.Field)
        {
            case "messages":
                await ProcessMessagesFieldAsync(metaWabaId, change.Value);
                break;

            case "message_template_status_update":
                await ProcessTemplateStatusAsync(metaWabaId, change.Value);
                break;

            case "message_template_quality_update":
                await ProcessTemplateQualityAsync(metaWabaId, change.Value);
                break;

            case "phone_number_name_update":
                await ProcessPhoneNameUpdateAsync(metaWabaId, change.Value);
                break;

            case "phone_number_quality_update":
                await ProcessPhoneQualityUpdateAsync(metaWabaId, change.Value);
                break;

            case "account_review_update":
                await ProcessAccountReviewAsync(metaWabaId, change.Value);
                break;

            case "account_update":
                await ProcessAccountUpdateAsync(metaWabaId, change.Value);
                break;

            case "business_capability_update":
                await ProcessBusinessCapabilityAsync(metaWabaId, change.Value);
                break;

            default:
                _logger.LogInformation(
                    "Unhandled webhook field: {Field} — logged for future implementation",
                    change.Field);
                await LogSystemEventAsync(metaWabaId, change.Field,
                    "unhandled_field", change.Value);
                break;
        }
    }

    // ── MESSAGES field ────────────────────────────────────────
    // Contains both inbound messages AND outbound status updates

    private async Task ProcessMessagesFieldAsync(
        string metaWabaId, MetaWebhookValue value)
    {
        // Find WABA by Meta phone number ID
        var waba = await ResolveWabaAsync(
            metaWabaId, value.Metadata?.PhoneNumberId);

        if (waba == null) return;

        // Process outbound status updates
        if (value.Statuses?.Any() == true)
        {
            foreach (var status in value.Statuses)
                await ProcessStatusUpdateAsync(waba, status);
        }

        // Process inbound messages
        if (value.Messages?.Any() == true)
        {
            // Build name map from contacts array
            var nameMap = value.Contacts?
                .ToDictionary(c => c.WaId, c => c.Profile?.Name)
                ?? new Dictionary<string, string?>();

            foreach (var msg in value.Messages)
                await ProcessInboundMessageAsync(waba, msg, nameMap);
        }
    }

    // ── Status update (sent / delivered / read / failed) ──────

    private async Task ProcessStatusUpdateAsync(
        WabaAccount waba, MetaMessageStatus status)
    {
        // Find message by provider message ID
        var message = await _db.Messages
            .FirstOrDefaultAsync(m =>
                m.WabaId == waba.Id &&
                m.ProviderMessageId == status.Id);

        if (message == null)
        {
            _logger.LogDebug(
                "Status update for unknown message: {ProviderId}", status.Id);
            return;
        }

        var timestamp = UnixToUtc(status.Timestamp);

        switch (status.Status.ToLower())
        {
            case "sent":
                if (message.Status < MessageStatus.Sent)
                {
                    message.Status = MessageStatus.Sent;
                    message.SentAt = timestamp;
                }
                break;

            case "delivered":
                message.Status      = MessageStatus.Delivered;
                message.DeliveredAt = timestamp;
                break;

            case "read":
                message.Status = MessageStatus.Read;
                message.ReadAt = timestamp;
                if (!message.DeliveredAt.HasValue)
                    message.DeliveredAt = timestamp;
                break;

            case "failed":
                message.Status      = MessageStatus.Failed;
                message.FailedAt    = timestamp;
                message.FailureCode = status.Errors?.FirstOrDefault()?.Code.ToString();
                message.FailureReason = status.Errors?.FirstOrDefault()?.Title
                    ?? status.Errors?.FirstOrDefault()?.Message
                    ?? "Failed";
                break;

            case "deleted":
                // Customer deleted the message — just log it
                _logger.LogInformation(
                    "Message {Id} was deleted by recipient", message.Id);
                break;

            default:
                _logger.LogWarning(
                    "Unknown status: {Status} for message {Id}", status.Status, message.Id);
                break;
        }

        message.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Message {Id} status → {Status}", message.Id, status.Status);
    }

    // ── Inbound message from customer ─────────────────────────

    private async Task ProcessInboundMessageAsync(
        WabaAccount waba,
        MetaInboundMessage msg,
        Dictionary<string, string?> nameMap)
    {
        // Upsert contact
        var contact = await UpsertContactAsync(
            waba.Id, msg.From,
            nameMap.GetValueOrDefault(msg.From));

        // Get or create conversation
        var conversation = await GetOrCreateConversationAsync(waba.Id, contact.Id);

        // Extract content preview and type
        var (messageType, content, rawContent) = ExtractInboundContent(msg);

        // Create inbound message record
        var inbound = new Message
        {
            Id             = Guid.NewGuid(),
            WabaId         = waba.Id,
            ConversationId = conversation.Id,
            ContactId      = contact.Id,
            Direction      = MessageDirection.Inbound,
            MessageType    = messageType,
            Status         = MessageStatus.Read,   // inbound = already read by customer
            Content        = content,
            ProviderMessageId = msg.Id,
            Provider       = "meta",
            SentAt         = UnixToUtc(msg.Timestamp),
            ReadAt         = DateTime.UtcNow,
            RawPayload     = rawContent,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };

        // Handle media
        var mediaContent = GetMediaContent(msg);
        if (mediaContent != null)
        {
            inbound.MediaUrl      = null;  // fetch URL separately via Graph API
            inbound.MediaMimeType = mediaContent.MimeType;
            inbound.MetaMediaId   = mediaContent.Id;
        }

        _db.Messages.Add(inbound);

        // Update conversation
        conversation.MessageCount++;
        conversation.UnreadCount++;
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UpdatedAt     = DateTime.UtcNow;

        // Reopen conversation if it was resolved
        if (conversation.Status == ConversationStatus.Resolved)
        {
            conversation.Status     = ConversationStatus.Open;
            conversation.ResolvedAt = null;
        }

        // Update contact stats
        contact.TotalMessagesReceived++;
        contact.LastMessageAt = DateTime.UtcNow;
        if (!contact.FirstMessageAt.HasValue)
            contact.FirstMessageAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Inbound {Type} from {Phone} on WABA {WabaId}",
            messageType, msg.From, waba.Id);
    }

    // ── Template status update ────────────────────────────────

    private async Task ProcessTemplateStatusAsync(
        string metaWabaId, MetaWebhookValue value)
    {
        if (value.MessageTemplateName == null) return;

        var waba = await ResolveWabaAsync(metaWabaId, null);
        if (waba == null) return;

        // Find template by Meta template ID or name
        var template = await _db.Templates
            .FirstOrDefaultAsync(t =>
                t.WabaId == waba.Id &&
                (t.MetaTemplateId == value.MessageTemplateId.ToString() ||
                 t.Name == value.MessageTemplateName));

        if (template == null)
        {
            _logger.LogWarning(
                "Template status update for unknown template: {Name}",
                value.MessageTemplateName);
            return;
        }

        var eventType = value.Event?.ToUpper();

        switch (eventType)
        {
            case "APPROVED":
                template.Status     = TemplateStatus.Approved;
                template.ApprovedAt = DateTime.UtcNow;
                break;

            case "REJECTED":
                template.Status           = TemplateStatus.Rejected;
                template.RejectedAt       = DateTime.UtcNow;
                template.RejectionReason  = value.Reason;
                break;

            case "PENDING_DELETION":
            case "DELETED":
                template.Status = TemplateStatus.Paused;
                break;

            case "FLAGGED":
            case "PAUSED":
            case "DISABLED":
                template.Status = TemplateStatus.Paused;
                break;

            case "IN_APPEAL":
                // Keep current status, just log
                break;

            default:
                _logger.LogWarning(
                    "Unknown template event: {Event}", value.Event);
                break;
        }

        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Template {Name} status → {Event}", value.MessageTemplateName, value.Event);
    }

    // ── Template quality update ───────────────────────────────

    private async Task ProcessTemplateQualityAsync(
        string metaWabaId, MetaWebhookValue value)
    {
        _logger.LogInformation(
            "Template quality update: name={Name} prev={Prev} curr={Curr}",
            value.MessageTemplateName,
            value.PreviousQualityScore,
            value.CurrentQualityScore);

        await LogSystemEventAsync(metaWabaId, "message_template_quality_update",
            value.Event ?? "quality_change", value);
    }

    // ── Phone number display name update ──────────────────────

    private async Task ProcessPhoneNameUpdateAsync(
        string metaWabaId, MetaWebhookValue value)
    {
        var waba = await _db.WabaAccounts
            .FirstOrDefaultAsync(w => w.PhoneNumberId == value.PhoneNumberId);

        if (waba == null) return;

        var eventType = value.Event?.ToUpper();

        switch (eventType)
        {
            case "APPROVED":
                if (!string.IsNullOrEmpty(value.RequestedVerifiedName))
                    waba.BusinessName = value.RequestedVerifiedName;
                _logger.LogInformation(
                    "Display name approved for WABA {Id}: {Name}",
                    waba.Id, value.RequestedVerifiedName);
                break;

            case "REJECTED":
                _logger.LogWarning(
                    "Display name rejected for WABA {Id}. Reason: {Reason}",
                    waba.Id, value.RejectionReason);
                break;
        }

        waba.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ── Phone number quality update ───────────────────────────

    private async Task ProcessPhoneQualityUpdateAsync(
        string metaWabaId, MetaWebhookValue value)
    {
        var waba = await _db.WabaAccounts
            .FirstOrDefaultAsync(w => w.PhoneNumberId == value.PhoneNumberId);

        if (waba == null) return;

        // Update quality rating
        if (!string.IsNullOrEmpty(value.CurrentQualityScore))
            waba.QualityRating = value.CurrentQualityScore;

        // Update messaging tier if limit changed
        if (!string.IsNullOrEmpty(value.CurrentLimit))
            waba.MessagingTier = value.CurrentLimit;

        waba.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "WABA {Id} quality {Prev} → {Curr} tier={Tier}",
            waba.Id,
            value.PreviousQualityScore,
            value.CurrentQualityScore,
            value.CurrentLimit);
    }

    // ── Account review update ─────────────────────────────────

    private async Task ProcessAccountReviewAsync(
        string metaWabaId, MetaWebhookValue value)
    {
        var waba = await ResolveWabaAsync(metaWabaId, null);
        if (waba == null) return;

        var decision = value.Event?.ToUpper();

        switch (decision)
        {
            case "APPROVED":
                waba.Status    = WabaStatus.Active;
                waba.IsActive  = true;
                break;

            case "REJECTED":
                waba.Status   = WabaStatus.Suspended;
                waba.IsActive = false;
                break;
        }

        waba.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "WABA {Id} account review: {Decision}", waba.Id, decision);
    }

    // ── Account update (ban, restriction) ────────────────────

    private async Task ProcessAccountUpdateAsync(
        string metaWabaId, MetaWebhookValue value)
    {
        var waba = await ResolveWabaAsync(metaWabaId, null);
        if (waba == null) return;

        // Handle ban
        if (value.BanInfo != null)
        {
            var banState = value.BanInfo.WabaBanState?.ToUpper();
            switch (banState)
            {
                case "SCHEDULE_FOR_DISABLE":
                    _logger.LogWarning(
                        "WABA {Id} scheduled for disable on {Date}",
                        waba.Id, value.BanInfo.WabaBanDate);
                    break;

                case "DISABLE":
                    waba.Status   = WabaStatus.Suspended;
                    waba.IsActive = false;
                    _logger.LogError("WABA {Id} has been DISABLED by Meta", waba.Id);
                    break;

                case "REINSTATE":
                    waba.Status   = WabaStatus.Active;
                    waba.IsActive = true;
                    _logger.LogInformation("WABA {Id} has been reinstated", waba.Id);
                    break;
            }
        }

        // Handle restrictions
        if (value.RestrictionInfo?.Any() == true)
        {
            _logger.LogWarning(
                "WABA {Id} restrictions: {Types}",
                waba.Id,
                string.Join(", ", value.RestrictionInfo
                    .Select(r => r.RestrictionType)));
        }

        // Handle account status
        if (!string.IsNullOrEmpty(value.AccountStatus))
        {
            waba.Status = value.AccountStatus.ToUpper() == "CONNECTED"
                ? WabaStatus.Active
                : WabaStatus.Disconnected;
            waba.IsActive = waba.Status == WabaStatus.Active;
        }

        waba.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ── Business capability update ────────────────────────────

    private async Task ProcessBusinessCapabilityAsync(
        string metaWabaId, MetaWebhookValue value)
    {
        var waba = await ResolveWabaAsync(metaWabaId, null);
        if (waba == null) return;

        if (value.MaxDailyConversationPerPhone.HasValue)
            waba.DailyMessageLimit = value.MaxDailyConversationPerPhone.Value;

        waba.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "WABA {Id} capability update: dailyLimit={Limit}",
            waba.Id, value.MaxDailyConversationPerPhone);
    }

    // ── Private helpers ───────────────────────────────────────

    private async Task<WabaAccount?> ResolveWabaAsync(
        string metaWabaId, string? phoneNumberId)
    {
        WabaAccount? waba = null;

        // Try by phone number ID first (more specific)
        if (!string.IsNullOrEmpty(phoneNumberId))
            waba = await _db.WabaAccounts
                .FirstOrDefaultAsync(w => w.PhoneNumberId == phoneNumberId);

        // Fall back to WABA ID
        if (waba == null && !string.IsNullOrEmpty(metaWabaId))
            waba = await _db.WabaAccounts
                .FirstOrDefaultAsync(w => w.WabaId == metaWabaId);

        if (waba == null)
            _logger.LogWarning(
                "Could not resolve WABA for metaWabaId={MetaId} phoneNumberId={PhoneId}",
                metaWabaId, phoneNumberId);

        return waba;
    }

    private async Task<Contact> UpsertContactAsync(
        Guid wabaId, string phoneNumber, string? name)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c =>
                c.WabaId == wabaId && c.PhoneNumber == phoneNumber);

        if (contact != null)
        {
            // Update name if we now have it
            if (!string.IsNullOrEmpty(name) && contact.Name == null)
            {
                contact.Name      = name;
                contact.UpdatedAt = DateTime.UtcNow;
            }
            return contact;
        }

        contact = new Contact
        {
            Id          = Guid.NewGuid(),
            WabaId      = wabaId,
            PhoneNumber = phoneNumber,
            Name        = name,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        _db.Contacts.Add(contact);
        return contact;
    }

    private async Task<Conversation> GetOrCreateConversationAsync(
        Guid wabaId, Guid contactId)
    {
        var existing = await _db.Conversations
            .FirstOrDefaultAsync(c =>
                c.WabaId    == wabaId &&
                c.ContactId == contactId &&
                c.Status    == ConversationStatus.Open);

        if (existing != null) return existing;

        var conv = new Conversation
        {
            Id            = Guid.NewGuid(),
            WabaId        = wabaId,
            ContactId     = contactId,
            Status        = ConversationStatus.Open,
            Channel       = "whatsapp",
            LastMessageAt = DateTime.UtcNow,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };

        _db.Conversations.Add(conv);
        return conv;
    }

    private async Task LogSystemEventAsync(
        string metaWabaId, string field, string eventType, object data)
    {
        // Resolve tenant for audit log
        var waba = await ResolveWabaAsync(metaWabaId, null);
        if (waba == null) return;

        _db.AuditLogs.Add(new AuditLog
        {
            TenantId   = waba.TenantId,
            WabaId     = waba.Id,
            Action     = $"webhook.{field}.{eventType}",
            EntityType = "WabaAccount",
            EntityId   = waba.Id.ToString(),
            NewValues  = JsonSerializer.Serialize(data, JsonOpts),
            CreatedAt  = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();
    }

    // ── Content extraction ────────────────────────────────────

    private static (MessageType type, string? content, string? rawJson)
        ExtractInboundContent(MetaInboundMessage msg)
    {
        var raw = JsonSerializer.Serialize(msg, JsonOpts);

        return msg.Type.ToLower() switch
        {
            "text"        => (MessageType.Text,     msg.Text?.Body, raw),
            "image"       => (MessageType.Image,    msg.Image?.Caption ?? "[Image]", raw),
            "video"       => (MessageType.Video,    msg.Video?.Caption ?? "[Video]", raw),
            "audio"       => (MessageType.Audio,    "[Audio]", raw),
            "document"    => (MessageType.Document, msg.Document?.FileName ?? "[Document]", raw),
            "sticker"     => (MessageType.Sticker,  "[Sticker]", raw),
            "location"    => (MessageType.Location,
                              $"[Location] {msg.Location?.Name ?? $"{msg.Location?.Latitude},{msg.Location?.Longitude}"}", raw),
            "contacts"    => (MessageType.Contact,  "[Contact Card]", raw),
            "button"      => (MessageType.Text,     msg.Button?.Text ?? msg.Button?.Payload, raw),
            "interactive" => (MessageType.Text,
                              msg.Interactive?.ButtonReply?.Title
                              ?? msg.Interactive?.ListReply?.Title
                              ?? "[Interactive Reply]", raw),
            "reaction"    => (MessageType.Reaction, $"[Reaction] {msg.Reaction?.Emoji}", raw),
            _             => (MessageType.Text,     $"[{msg.Type}]", raw),
        };
    }

    private static MetaMediaContent? GetMediaContent(MetaInboundMessage msg) =>
        msg.Type.ToLower() switch
        {
            "image"    => msg.Image,
            "video"    => msg.Video,
            "audio"    => msg.Audio,
            "document" => msg.Document,
            "sticker"  => msg.Sticker,
            _          => null
        };

    private static DateTime UnixToUtc(string timestamp)
    {
        if (long.TryParse(timestamp, out var unix))
            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        return DateTime.UtcNow;
    }
}
