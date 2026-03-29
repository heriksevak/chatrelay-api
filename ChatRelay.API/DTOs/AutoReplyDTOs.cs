// ============================================================
//  ChatRelay — AutoReply DTOs
// ============================================================

using ChatRelay.Models;
using System.ComponentModel.DataAnnotations;

namespace ChatRelay.API.DTOs;

// ── Create / Update Rule ──────────────────────────────────────

public class AutoReplyRuleRequest
{
    [Required, MaxLength(200)]
    public string RuleName { get; set; } = string.Empty;

    public TriggerType TriggerType { get; set; } = TriggerType.Keyword;

    // Keywords — required when TriggerType = Keyword
    public List<string>? Keywords { get; set; }
    public ChatRelay.Models.MatchType MatchType { get; set; } = ChatRelay.Models.MatchType.Contains;

    // Reply
    public ReplyType ReplyType { get; set; } = ReplyType.Text;

    [MaxLength(4096)]
    public string? ReplyContent { get; set; }   // for Text reply

    public Guid? TemplateId { get; set; }        // for Template reply
    public List<string>? TemplateParams { get; set; }

    // Control
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;       // lower = higher priority

    // Schedule — only trigger during these hours (optional)
    public TimeOnly? ActiveFromTime { get; set; }
    public TimeOnly? ActiveUntilTime { get; set; }

    [MaxLength(50)]
    public string? ActiveTimezone { get; set; }

    // Days bitmask: 1=Mon, 2=Tue, 4=Wed, 8=Thu, 16=Fri, 32=Sat, 64=Sun
    public int? ActiveDaysBitmask { get; set; }

    // Throttle — don't reply to same contact more than once per X minutes
    public int? ThrottleMinutes { get; set; }
}

// ── Response ──────────────────────────────────────────────────

public class AutoReplyRuleResponse
{
    public Guid Id { get; set; }
    public Guid WabaId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public List<string>? Keywords { get; set; }
    public string MatchType { get; set; } = string.Empty;
    public string ReplyType { get; set; } = string.Empty;
    public string? ReplyContent { get; set; }
    public Guid? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; }
    public int TriggerCount { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public int? ThrottleMinutes { get; set; }
    public TimeOnly? ActiveFromTime { get; set; }
    public TimeOnly? ActiveUntilTime { get; set; }
    public int? ActiveDaysBitmask { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ── Test rule request ─────────────────────────────────────────

public class TestRuleRequest
{
    [Required]
    public string InboundMessage { get; set; } = string.Empty;
}

public class TestRuleResponse
{
    public bool Matched { get; set; }
    public Guid? MatchedRuleId { get; set; }
    public string? MatchedRuleName { get; set; }
    public string? ReplyPreview { get; set; }
    public string? NoMatchReason { get; set; }
}
