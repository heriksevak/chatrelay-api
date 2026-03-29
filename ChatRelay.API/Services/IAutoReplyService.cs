// ============================================================
//  ChatRelay — IAutoReplyService + AutoReplyService
// ============================================================

using ChatRelay.API.Context;
using ChatRelay.API.Controllers;
using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChatRelay.API.Services;

public interface IAutoReplyService
{
    Task<ServiceResult<AutoReplyRuleResponse>> CreateRuleAsync(
        Guid wabaId, AutoReplyRuleRequest request);

    Task<ServiceResult<List<AutoReplyRuleResponse>>> GetRulesAsync(Guid wabaId);

    Task<ServiceResult<AutoReplyRuleResponse>> GetRuleByIdAsync(
        Guid ruleId, Guid wabaId);

    Task<ServiceResult<AutoReplyRuleResponse>> UpdateRuleAsync(
        Guid ruleId, Guid wabaId, AutoReplyRuleRequest request);

    Task<ServiceResult<bool>> DeleteRuleAsync(Guid ruleId, Guid wabaId);

    Task<ServiceResult<bool>> ToggleRuleAsync(Guid ruleId, Guid wabaId, bool isActive);

    Task<ServiceResult<bool>> ReorderRulesAsync(
        Guid wabaId, List<RulePriorityUpdate> updates);
}

public class AutoReplyService : IAutoReplyService
{
    private readonly ApplicationDbContext _db;

    public AutoReplyService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceResult<AutoReplyRuleResponse>> CreateRuleAsync(
        Guid wabaId, AutoReplyRuleRequest request)
    {
        // Validate keyword rules have keywords
        if (request.TriggerType == TriggerType.Keyword &&
            (request.Keywords == null || !request.Keywords.Any()))
            return ServiceResult<AutoReplyRuleResponse>.Fail(
                "Keywords are required when TriggerType is Keyword");

        // Validate template reply has template
        if (request.ReplyType == ReplyType.Template && !request.TemplateId.HasValue)
            return ServiceResult<AutoReplyRuleResponse>.Fail(
                "TemplateId is required when ReplyType is Template");

        // Validate text reply has content
        if (request.ReplyType == ReplyType.Text &&
            string.IsNullOrWhiteSpace(request.ReplyContent))
            return ServiceResult<AutoReplyRuleResponse>.Fail(
                "ReplyContent is required when ReplyType is Text");

        // Check duplicate rule name for this WABA
        var nameExists = await _db.AutoReplyRules
            .AnyAsync(r => r.WabaId == wabaId && r.RuleName == request.RuleName);

        if (nameExists)
            return ServiceResult<AutoReplyRuleResponse>.Fail(
                "A rule with this name already exists for this WABA");

        var rule = new AutoReplyRule
        {
            Id = Guid.NewGuid(),
            WabaId = wabaId,
            RuleName = request.RuleName.Trim(),
            TriggerType = request.TriggerType,
            Keywords = request.Keywords != null
                                ? JsonSerializer.Serialize(request.Keywords)
                                : null,
            MatchType = request.MatchType,
            ReplyType = request.ReplyType,
            ReplyContent = request.ReplyContent,
            TemplateId = request.TemplateId,
            TemplateParams = request.TemplateParams != null
                                ? JsonSerializer.Serialize(request.TemplateParams)
                                : null,
            IsActive = request.IsActive,
            Priority = request.Priority,
            ThrottleMinutes = request.ThrottleMinutes,
            ActiveFromTime = request.ActiveFromTime,
            ActiveUntilTime = request.ActiveUntilTime,
            ActiveTimezone = request.ActiveTimezone,
            ActiveDaysBitmask = request.ActiveDaysBitmask,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.AutoReplyRules.Add(rule);
        await _db.SaveChangesAsync();

        await _db.Entry(rule).Reference(r => r.Template).LoadAsync();
        return ServiceResult<AutoReplyRuleResponse>.Ok(ToResponse(rule));
    }

    public async Task<ServiceResult<List<AutoReplyRuleResponse>>> GetRulesAsync(Guid wabaId)
    {
        var rules = await _db.AutoReplyRules
            .Include(r => r.Template)
            .Where(r => r.WabaId == wabaId)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();

        return ServiceResult<List<AutoReplyRuleResponse>>.Ok(
            rules.Select(ToResponse).ToList());
    }

    public async Task<ServiceResult<AutoReplyRuleResponse>> GetRuleByIdAsync(
        Guid ruleId, Guid wabaId)
    {
        var rule = await _db.AutoReplyRules
            .Include(r => r.Template)
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.WabaId == wabaId);

        if (rule == null)
            return ServiceResult<AutoReplyRuleResponse>.Fail("Rule not found");

        return ServiceResult<AutoReplyRuleResponse>.Ok(ToResponse(rule));
    }

    public async Task<ServiceResult<AutoReplyRuleResponse>> UpdateRuleAsync(
        Guid ruleId, Guid wabaId, AutoReplyRuleRequest request)
    {
        var rule = await _db.AutoReplyRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.WabaId == wabaId);

        if (rule == null)
            return ServiceResult<AutoReplyRuleResponse>.Fail("Rule not found");

        // Validate
        if (request.TriggerType == TriggerType.Keyword &&
            (request.Keywords == null || !request.Keywords.Any()))
            return ServiceResult<AutoReplyRuleResponse>.Fail(
                "Keywords are required when TriggerType is Keyword");

        if (request.ReplyType == ReplyType.Template && !request.TemplateId.HasValue)
            return ServiceResult<AutoReplyRuleResponse>.Fail(
                "TemplateId is required when ReplyType is Template");

        if (request.ReplyType == ReplyType.Text &&
            string.IsNullOrWhiteSpace(request.ReplyContent))
            return ServiceResult<AutoReplyRuleResponse>.Fail(
                "ReplyContent is required when ReplyType is Text");

        rule.RuleName = request.RuleName.Trim();
        rule.TriggerType = request.TriggerType;
        rule.Keywords = request.Keywords != null
                                ? JsonSerializer.Serialize(request.Keywords)
                                : null;
        rule.MatchType = request.MatchType;
        rule.ReplyType = request.ReplyType;
        rule.ReplyContent = request.ReplyContent;
        rule.TemplateId = request.TemplateId;
        rule.TemplateParams = request.TemplateParams != null
                                ? JsonSerializer.Serialize(request.TemplateParams)
                                : null;
        rule.IsActive = request.IsActive;
        rule.Priority = request.Priority;
        rule.ThrottleMinutes = request.ThrottleMinutes;
        rule.ActiveFromTime = request.ActiveFromTime;
        rule.ActiveUntilTime = request.ActiveUntilTime;
        rule.ActiveTimezone = request.ActiveTimezone;
        rule.ActiveDaysBitmask = request.ActiveDaysBitmask;
        rule.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _db.Entry(rule).Reference(r => r.Template).LoadAsync();

        return ServiceResult<AutoReplyRuleResponse>.Ok(ToResponse(rule));
    }

    public async Task<ServiceResult<bool>> DeleteRuleAsync(Guid ruleId, Guid wabaId)
    {
        var rule = await _db.AutoReplyRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.WabaId == wabaId);

        if (rule == null)
            return ServiceResult<bool>.Fail("Rule not found");

        _db.AutoReplyRules.Remove(rule);
        await _db.SaveChangesAsync();

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> ToggleRuleAsync(
        Guid ruleId, Guid wabaId, bool isActive)
    {
        var rule = await _db.AutoReplyRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.WabaId == wabaId);

        if (rule == null)
            return ServiceResult<bool>.Fail("Rule not found");

        rule.IsActive = isActive;
        rule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> ReorderRulesAsync(
        Guid wabaId, List<RulePriorityUpdate> updates)
    {
        foreach (var update in updates)
        {
            var rule = await _db.AutoReplyRules
                .FirstOrDefaultAsync(r => r.Id == update.RuleId && r.WabaId == wabaId);

            if (rule == null) continue;

            rule.Priority = update.Priority;
            rule.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(true);
    }

    private static AutoReplyRuleResponse ToResponse(AutoReplyRule r)
    {
        List<string>? keywords = null;
        if (!string.IsNullOrEmpty(r.Keywords))
        {
            try { keywords = JsonSerializer.Deserialize<List<string>>(r.Keywords); }
            catch { keywords = new List<string> { r.Keywords }; }
        }

        return new AutoReplyRuleResponse
        {
            Id = r.Id,
            WabaId = r.WabaId,
            RuleName = r.RuleName,
            TriggerType = r.TriggerType.ToString(),
            Keywords = keywords,
            MatchType = r.MatchType.ToString(),
            ReplyType = r.ReplyType.ToString(),
            ReplyContent = r.ReplyContent,
            TemplateId = r.TemplateId,
            TemplateName = r.Template?.Name,
            IsActive = r.IsActive,
            Priority = r.Priority,
            TriggerCount = r.TriggerCount,
            LastTriggeredAt = r.LastTriggeredAt,
            ThrottleMinutes = r.ThrottleMinutes,
            ActiveFromTime = r.ActiveFromTime,
            ActiveUntilTime = r.ActiveUntilTime,
            ActiveDaysBitmask = r.ActiveDaysBitmask,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
        };
    }
}

// ── Priority reorder DTO ──────────────────────────────────────

public class RulePriorityUpdate
{
    public Guid RuleId { get; set; }
    public int Priority { get; set; }
}


// ============================================================
//  ChatRelay — AutoReplyController
// ============================================================

