// ============================================================
//  ChatRelay — WABA DTOs
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace ChatRelay.API.DTOs;

// ── Onboard WABA via Facebook Embedded Signup ─────────────────

public class WabaOnboardRequest
{
    // From Facebook Embedded Signup SDK callback
    [Required]
    public string Code { get; set; } = string.Empty;           // exchange for access token

    [Required]
    public string WabaId { get; set; } = string.Empty;         // WhatsApp Business Account ID

    [Required]
    public string PhoneNumberId { get; set; } = string.Empty;  // phone number ID from Meta

    // Tenant enters this manually — their AiSensy account key
    [Required]
    public string AiSensyApiKey { get; set; } = string.Empty;

    // Optional — display name shown in dashboard
    [MaxLength(200)]
    public string? DisplayName { get; set; }
}

// ── Manual WABA (fallback if SDK not used yet) ────────────────

public class WabaManualRequest
{
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string PhoneNumberId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string WabaId { get; set; } = string.Empty;

    [Required]
    public string AiSensyApiKey { get; set; } = string.Empty;

    [Required]
    public string MetaAccessToken { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string MetaAppSecret { get; set; } = string.Empty;
}

// ── Update WABA ───────────────────────────────────────────────

public class UpdateWabaRequest
{
    [MaxLength(200)] public string? DisplayName { get; set; }
    [MaxLength(200)] public string? BusinessName { get; set; }
    [MaxLength(500)] public string? BusinessDescription { get; set; }
    [MaxLength(200)] public string? BusinessWebsite { get; set; }
    [MaxLength(100)] public string? BusinessCategory { get; set; }
    [MaxLength(200)] public string? BusinessEmail { get; set; }

    // Allow updating AiSensy key if they switch accounts
    public string? AiSensyApiKey { get; set; }

    public int? DailyMessageLimit { get; set; }
    public int? MonthlyMessageLimit { get; set; }
}

// ── WABA Response ─────────────────────────────────────────────

public class WabaResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string WabaId { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string? BusinessDescription { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? QualityRating { get; set; }
    public string? MessagingTier { get; set; }
    public int DailyMessageLimit { get; set; }
    public int MonthlyMessageLimit { get; set; }
    public int CurrentMonthMessageCount { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Never expose raw credentials — only show masked versions
    public string AiSensyKeyMasked { get; set; } = string.Empty;   // "as_live_ab12****"
    public bool HasMetaToken { get; set; }                          // true/false only
}

// ── Credential validation result ──────────────────────────────

public class CredentialValidationResult
{
    public bool AiSensyValid { get; set; }
    public bool MetaTokenValid { get; set; }
    public string? AiSensyError { get; set; }
    public string? MetaError { get; set; }
    public string? BusinessName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? QualityRating { get; set; }
}
