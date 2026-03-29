// ============================================================
//  ChatRelay — Template DTOs
//  Covers every Meta template type:
//  Text, Image, Video, Document, Location headers
//  Quick reply, CTA (URL/Phone), Copy code buttons
//  Carousel templates
//  All component combinations
// ============================================================

using ChatRelay.Models;
using System.ComponentModel.DataAnnotations;

namespace ChatRelay.API.DTOs;

// ── Create / Update Template Request ─────────────────────────

public class CreateTemplateRequest
{
    // Template name — lowercase, underscores only, no spaces
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public TemplateCategory Category { get; set; } = TemplateCategory.Utility;

    [Required, MaxLength(10)]
    public string Language { get; set; } = "en";

    // Components
    public TemplateHeaderComponent? Header { get; set; }

    [Required]
    public TemplateBodyComponent Body { get; set; } = null!;

    public TemplateFooterComponent? Footer { get; set; }

    public List<TemplateButtonComponent>? Buttons { get; set; }

    // Carousel (alternative to header+body — standalone type)
    public List<TemplateCarouselCard>? CarouselCards { get; set; }
}

// ── Header Component ──────────────────────────────────────────

public class TemplateHeaderComponent
{
    // TEXT | IMAGE | VIDEO | DOCUMENT | LOCATION
    [Required]
    public string Format { get; set; } = string.Empty;

    // For TEXT format
    [MaxLength(60)]
    public string? Text { get; set; }

    // Text variables {{1}} count
    public List<string>? TextExamples { get; set; }

    // For IMAGE / VIDEO / DOCUMENT — example media URL for Meta review
    public string? MediaExampleUrl { get; set; }
}

// ── Body Component ────────────────────────────────────────────

public class TemplateBodyComponent
{
    [Required, MaxLength(1024)]
    public string Text { get; set; } = string.Empty;

    // Example values for each {{variable}} in order
    // e.g. body = "Hello {{1}}, your order {{2}} is ready"
    // examples = ["John", "ORD-123"]
    public List<string>? VariableExamples { get; set; }
}

// ── Footer Component ──────────────────────────────────────────

public class TemplateFooterComponent
{
    [Required, MaxLength(60)]
    public string Text { get; set; } = string.Empty;
}

// ── Button Components ─────────────────────────────────────────

public class TemplateButtonComponent
{
    // QUICK_REPLY | URL | PHONE_NUMBER | COPY_CODE | OTP | VOICE_CALL
    [Required]
    public string Type { get; set; } = string.Empty;

    [Required, MaxLength(25)]
    public string Text { get; set; } = string.Empty;

    // For URL buttons
    [MaxLength(2000)]
    public string? Url { get; set; }

    // URL type: STATIC | DYNAMIC (dynamic = has {{1}} variable)
    public string? UrlType { get; set; }

    // Example for dynamic URL suffix
    public string? UrlExample { get; set; }

    // For PHONE_NUMBER buttons
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    // For COPY_CODE buttons
    [MaxLength(15)]
    public string? CouponCode { get; set; }

    // For OTP buttons — COPY_CODE | ONE_TAP | ZERO_TAP
    public string? OtpType { get; set; }

    // For ONE_TAP OTP
    public string? PackageName { get; set; }
    public string? SignatureHash { get; set; }
}

// ── Carousel Card ─────────────────────────────────────────────

public class TemplateCarouselCard
{
    // Each card has header (IMAGE or VIDEO), body, and buttons
    [Required]
    public TemplateCarouselHeader Header { get; set; } = null!;

    [Required]
    public TemplateBodyComponent Body { get; set; } = null!;

    // Max 2 buttons per card
    public List<TemplateButtonComponent>? Buttons { get; set; }
}

public class TemplateCarouselHeader
{
    // IMAGE | VIDEO
    [Required]
    public string Format { get; set; } = string.Empty;

    // Example media for Meta review
    public string? MediaExampleUrl { get; set; }
}

// ── Template Response ─────────────────────────────────────────

public class TemplateResponse
{
    public Guid Id { get; set; }
    public Guid WabaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MetaTemplateId { get; set; }
    public string? RejectionReason { get; set; }

    // Parsed components
    public string? HeaderType { get; set; }
    public string? HeaderContent { get; set; }
    public string BodyText { get; set; } = string.Empty;
    public string? FooterText { get; set; }
    public string? ButtonsJson { get; set; }

    // Stats
    public int TimesUsed { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ── Template Preview Response ─────────────────────────────────

public class TemplatePreviewResponse
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? HeaderType { get; set; }
    public string? HeaderText { get; set; }
    public string BodyText { get; set; } = string.Empty;
    public string? FooterText { get; set; }
    public List<PreviewButton> Buttons { get; set; } = new();
    public List<PreviewCarouselCard> CarouselCards { get; set; } = new();

    // Rendered preview with example values filled in
    public string RenderedBody { get; set; } = string.Empty;
    public string? RenderedHeader { get; set; }

    // Meta submission payload preview
    public object MetaPayload { get; set; } = new();
}

public class PreviewButton
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? PhoneNumber { get; set; }
}

public class PreviewCarouselCard
{
    public int Index { get; set; }
    public string HeaderFormat { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public List<PreviewButton> Buttons { get; set; } = new();
}

// ── Filter ────────────────────────────────────────────────────

public class TemplateFilterRequest
{
    public string? Status { get; set; }    // Pending, Approved, Rejected
    public string? Category { get; set; } // Marketing, Utility, Authentication
    public string? Language { get; set; }
    public string? Search { get; set; }   // search by name
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ── Sync Response ─────────────────────────────────────────────

public class TemplateSyncResponse
{
    public int TotalFromMeta { get; set; }
    public int NewTemplates { get; set; }
    public int UpdatedTemplates { get; set; }
    public int NoChange { get; set; }
    public List<string> Errors { get; set; } = new();
}
