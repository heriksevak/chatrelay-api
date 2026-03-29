// ============================================================
//  ChatRelay — Message DTOs
//  Covers all AiSensy/WhatsApp message types
// ============================================================

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ChatRelay.API.DTOs;

// ── Send Message Request (top-level) ──────────────────────────

public class SendMessageRequest
{
    [Required, MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;  // e.g. +919876543210

    [Required]
    public MessagePayload Payload { get; set; } = null!;

    // Optional — schedule for future delivery
    public DateTime? ScheduledAt { get; set; }

    // Optional — link to existing conversation
    public Guid? ConversationId { get; set; }
}

// ── Message Payload — discriminated union by Type ─────────────

public class MessagePayload
{
    [Required]
    public MessagePayloadType Type { get; set; }

    // Text
    public TextPayload? Text { get; set; }

    // Media (image, video, audio, document, sticker)
    public MediaPayload? Media { get; set; }

    // Location
    public LocationPayload? Location { get; set; }

    // Contact card
    public ContactPayload? Contact { get; set; }

    // Template (all template types handled here)
    public TemplatePayload? Template { get; set; }

    // Interactive (buttons, list)
    public InteractivePayload? Interactive { get; set; }
}

public enum MessagePayloadType
{
    Text,
    Image,
    Video,
    Audio,
    Document,
    Sticker,
    Location,
    Contact,
    Template,
    Interactive
}

// ── Text ──────────────────────────────────────────────────────

public class TextPayload
{
    [Required, MaxLength(4096)]
    public string Body { get; set; } = string.Empty;

    public bool PreviewUrl { get; set; } = false;
}

// ── Media (Image / Video / Audio / Document / Sticker) ────────

public class MediaPayload
{
    // Either provide a URL or a MetaMediaId (uploaded via Graph API)
    public string? Url { get; set; }
    public string? MetaMediaId { get; set; }

    [MaxLength(1024)]
    public string? Caption { get; set; }   // image, video, document only

    [MaxLength(200)]
    public string? FileName { get; set; }  // document only
}

// ── Location ──────────────────────────────────────────────────

public class LocationPayload
{
    [Required]
    public double Latitude { get; set; }

    [Required]
    public double Longitude { get; set; }

    [MaxLength(200)] public string? Name { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
}

// ── Contact Card ──────────────────────────────────────────────

public class ContactPayload
{
    [Required]
    public List<WhatsAppContact> Contacts { get; set; } = new();
}

public class WhatsAppContact
{
    public ContactName Name { get; set; } = null!;
    public List<ContactPhone>? Phones { get; set; }
    public List<ContactEmail>? Emails { get; set; }
    public List<ContactUrl>? Urls { get; set; }
    public ContactOrg? Org { get; set; }
}

public class ContactName
{
    [Required] public string FormattedName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class ContactPhone
{
    public string? Phone { get; set; }
    public string? Type { get; set; }  // CELL, MAIN, IPHONE, HOME, WORK
    public string? WaId { get; set; }
}

public class ContactEmail
{
    public string? Email { get; set; }
    public string? Type { get; set; }  // HOME, WORK
}

public class ContactUrl
{
    public string? Url { get; set; }
    public string? Type { get; set; }  // HOME, WORK
}

public class ContactOrg
{
    public string? Company { get; set; }
    public string? Title { get; set; }
}

// ── Template ──────────────────────────────────────────────────
// Covers: text-only, header image/video/document,
//         body params, footer, buttons (CTA / quick reply)

public class TemplatePayload
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;   // approved template name

    [Required, MaxLength(10)]
    public string Language { get; set; } = "en";       // e.g. "en", "en_US", "hi"

    // Header component (optional)
    public TemplateHeader? Header { get; set; }

    // Body parameters {{1}}, {{2}} etc.
    public List<TemplateParam>? BodyParams { get; set; }

    // Button parameters (url suffix or quick reply payload)
    public List<TemplateButtonParam>? ButtonParams { get; set; }
}

public class TemplateHeader
{
    // "text" | "image" | "video" | "document" | "location"
    [Required]
    public string Type { get; set; } = string.Empty;

    // For text header
    public List<TemplateParam>? TextParams { get; set; }

    // For media header
    public string? MediaUrl { get; set; }
    public string? MetaMediaId { get; set; }

    // For location header
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationName { get; set; }
    public string? LocationAddress { get; set; }
}

public class TemplateParam
{
    // "text" | "currency" | "date_time" | "image" | "document" | "video"
    [Required]
    public string Type { get; set; } = "text";

    public string? Text { get; set; }

    // For currency type
    public TemplateCurrency? Currency { get; set; }

    // For date_time type
    public TemplateDateTime? DateTime { get; set; }
}

public class TemplateCurrency
{
    public string FallbackValue { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;   // USD, INR etc.
    public long Amount1000 { get; set; }                // amount × 1000
}

public class TemplateDateTime
{
    public string FallbackValue { get; set; } = string.Empty;
}

public class TemplateButtonParam
{
    public int Index { get; set; }        // button index (0-based)
    public string Type { get; set; } = string.Empty;  // "url" | "quick_reply" | "copy_code"
    public string? Payload { get; set; }  // URL suffix or quick reply payload
}

// ── Interactive ───────────────────────────────────────────────
// Covers: button messages (up to 3) and list messages (up to 10 rows)

public class InteractivePayload
{
    // "button" | "list" | "product" | "product_list"
    [Required]
    public string Type { get; set; } = string.Empty;

    public InteractiveHeader? Header { get; set; }

    [Required, MaxLength(1024)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(60)]
    public string? Footer { get; set; }

    // For type = "button"
    public List<InteractiveButton>? Buttons { get; set; }

    // For type = "list"
    public InteractiveList? List { get; set; }
}

public class InteractiveHeader
{
    // "text" | "image" | "video" | "document"
    public string Type { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? MediaUrl { get; set; }
    public string? MetaMediaId { get; set; }
}

public class InteractiveButton
{
    public string Id { get; set; } = string.Empty;      // max 256 chars
    public string Title { get; set; } = string.Empty;   // max 20 chars
}

public class InteractiveList
{
    [MaxLength(20)]
    public string ButtonText { get; set; } = string.Empty;

    public List<InteractiveSection> Sections { get; set; } = new();
}

public class InteractiveSection
{
    [MaxLength(24)]
    public string? Title { get; set; }

    public List<InteractiveRow> Rows { get; set; } = new();
}

public class InteractiveRow
{
    [MaxLength(200)] public string Id { get; set; } = string.Empty;
    [MaxLength(24)]  public string Title { get; set; } = string.Empty;
    [MaxLength(72)]  public string? Description { get; set; }
}

// ── Responses ─────────────────────────────────────────────────

public class MessageResponse
{
    public Guid Id { get; set; }
    public Guid WabaId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ContactId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BulkSendRequest
{
    [Required]
    public List<string> PhoneNumbers { get; set; } = new();  // max 1000

    [Required]
    public MessagePayload Payload { get; set; } = null!;

    public DateTime? ScheduledAt { get; set; }
}

public class BulkSendResponse
{
    public int Total { get; set; }
    public int Queued { get; set; }
    public int Failed { get; set; }
    public List<MessageResponse> Messages { get; set; } = new();
}
