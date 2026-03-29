// ============================================================
//  ChatRelay — Meta Webhook Payload Models
//  Covers every webhook Meta sends:
//  - Message status updates (sent, delivered, read, failed)
//  - Inbound messages (all types)
//  - Template status (approved, rejected, paused)
//  - Phone number events (quality, tier, name)
//  - WABA account events (banned, restriction)
//  - System notifications
// ============================================================

using System.Text.Json.Serialization;

namespace ChatRelay.API.Webhooks;

// ── Root payload ──────────────────────────────────────────────
// Every webhook from Meta has this structure

public class MetaWebhookPayload
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;  // "whatsapp_business_account"

    [JsonPropertyName("entry")]
    public List<MetaWebhookEntry> Entry { get; set; } = new();
}

public class MetaWebhookEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;  // WABA ID

    [JsonPropertyName("changes")]
    public List<MetaWebhookChange> Changes { get; set; } = new();
}

public class MetaWebhookChange
{
    [JsonPropertyName("value")]
    public MetaWebhookValue Value { get; set; } = null!;

    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;
    // "messages" | "message_template_status_update" |
    // "phone_number_name_update" | "phone_number_quality_update" |
    // "account_review_update" | "account_update" |
    // "business_capability_update" | "message_template_quality_update"
}

// ── Value — contains either messages or system events ─────────

public class MetaWebhookValue
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }  // "whatsapp"

    [JsonPropertyName("metadata")]
    public MetaWebhookMetadata? Metadata { get; set; }

    // ── Message events ──────────────────────────────────────
    [JsonPropertyName("contacts")]
    public List<MetaWebhookContact>? Contacts { get; set; }

    [JsonPropertyName("messages")]
    public List<MetaInboundMessage>? Messages { get; set; }

    [JsonPropertyName("statuses")]
    public List<MetaMessageStatus>? Statuses { get; set; }

    [JsonPropertyName("errors")]
    public List<MetaWebhookError>? Errors { get; set; }

    // ── Template events ─────────────────────────────────────
    [JsonPropertyName("message_template_id")]
    public long? MessageTemplateId { get; set; }

    [JsonPropertyName("message_template_name")]
    public string? MessageTemplateName { get; set; }

    [JsonPropertyName("message_template_language")]
    public string? MessageTemplateLanguage { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }
    // APPROVED | REJECTED | PENDING_DELETION | FLAGGED | PAUSED | DISABLED | IN_APPEAL

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    // ── Phone number quality ────────────────────────────────
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; set; }

    [JsonPropertyName("current_limit")]
    public string? CurrentLimit { get; set; }

    [JsonPropertyName("requested_verified_name")]
    public string? RequestedVerifiedName { get; set; }

    [JsonPropertyName("rejection_reason")]
    public string? RejectionReason { get; set; }

    // Quality update fields
    [JsonPropertyName("current_quality_score")]
    public string? CurrentQualityScore { get; set; }  // GREEN, YELLOW, RED

    [JsonPropertyName("previous_quality_score")]
    public string? PreviousQualityScore { get; set; }

    // ── Account events ──────────────────────────────────────
    [JsonPropertyName("ban_info")]
    public MetaBanInfo? BanInfo { get; set; }

    [JsonPropertyName("restriction_info")]
    public List<MetaRestrictionInfo>? RestrictionInfo { get; set; }

    [JsonPropertyName("account_status")]
    public string? AccountStatus { get; set; }  // CONNECTED, DISCONNECTED

    [JsonPropertyName("violations")]
    public List<MetaViolation>? Violations { get; set; }

    // ── Business capability ─────────────────────────────────
    [JsonPropertyName("max_daily_conversation_per_phone")]
    public int? MaxDailyConversationPerPhone { get; set; }

    [JsonPropertyName("max_phone_numbers_per_business")]
    public int? MaxPhoneNumbersPerBusiness { get; set; }
}

public class MetaWebhookMetadata
{
    [JsonPropertyName("display_phone_number")]
    public string DisplayPhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("phone_number_id")]
    public string PhoneNumberId { get; set; } = string.Empty;
}

public class MetaWebhookContact
{
    [JsonPropertyName("profile")]
    public MetaContactProfile? Profile { get; set; }

    [JsonPropertyName("wa_id")]
    public string WaId { get; set; } = string.Empty;  // customer WhatsApp ID
}

public class MetaContactProfile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

// ── Inbound message ───────────────────────────────────────────

public class MetaInboundMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;  // Meta message ID

    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;  // customer phone number

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    // text | image | video | audio | document | sticker |
    // location | contacts | button | interactive |
    // order | reaction | unsupported

    // Text
    [JsonPropertyName("text")]
    public MetaTextContent? Text { get; set; }

    // Media
    [JsonPropertyName("image")]
    public MetaMediaContent? Image { get; set; }

    [JsonPropertyName("video")]
    public MetaMediaContent? Video { get; set; }

    [JsonPropertyName("audio")]
    public MetaMediaContent? Audio { get; set; }

    [JsonPropertyName("document")]
    public MetaMediaContent? Document { get; set; }

    [JsonPropertyName("sticker")]
    public MetaMediaContent? Sticker { get; set; }

    // Location
    [JsonPropertyName("location")]
    public MetaLocationContent? Location { get; set; }

    // Contact card
    [JsonPropertyName("contacts")]
    public List<MetaContactCard>? Contacts { get; set; }

    // Button reply (quick reply button clicked)
    [JsonPropertyName("button")]
    public MetaButtonContent? Button { get; set; }

    // Interactive reply (list or reply button)
    [JsonPropertyName("interactive")]
    public MetaInteractiveContent? Interactive { get; set; }

    // Reaction
    [JsonPropertyName("reaction")]
    public MetaReactionContent? Reaction { get; set; }

    // Order (product message)
    [JsonPropertyName("order")]
    public MetaOrderContent? Order { get; set; }

    // Context (if reply to a message)
    [JsonPropertyName("context")]
    public MetaMessageContext? Context { get; set; }

    // Errors (unsupported message type)
    [JsonPropertyName("errors")]
    public List<MetaWebhookError>? Errors { get; set; }
}

public class MetaTextContent
{
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
}

public class MetaMediaContent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }  // Meta media ID

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("filename")]
    public string? FileName { get; set; }
}

public class MetaLocationContent
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

public class MetaContactCard
{
    [JsonPropertyName("name")]
    public MetaContactName? Name { get; set; }

    [JsonPropertyName("phones")]
    public List<MetaContactPhone>? Phones { get; set; }
}

public class MetaContactName
{
    [JsonPropertyName("formatted_name")]
    public string FormattedName { get; set; } = string.Empty;
}

public class MetaContactPhone
{
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class MetaButtonContent
{
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class MetaInteractiveContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;  // button_reply | list_reply

    [JsonPropertyName("button_reply")]
    public MetaButtonReply? ButtonReply { get; set; }

    [JsonPropertyName("list_reply")]
    public MetaListReply? ListReply { get; set; }
}

public class MetaButtonReply
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public class MetaListReply
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class MetaReactionContent
{
    [JsonPropertyName("message_id")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("emoji")]
    public string? Emoji { get; set; }  // empty string = reaction removed
}

public class MetaOrderContent
{
    [JsonPropertyName("catalog_id")]
    public string CatalogId { get; set; } = string.Empty;

    [JsonPropertyName("product_items")]
    public List<MetaOrderItem>? ProductItems { get; set; }
}

public class MetaOrderItem
{
    [JsonPropertyName("product_retailer_id")]
    public string ProductRetailerId { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("item_price")]
    public decimal ItemPrice { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;
}

public class MetaMessageContext
{
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }  // original message ID being replied to
}

// ── Message status update ─────────────────────────────────────

public class MetaMessageStatus
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;  // provider message ID

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    // sent | delivered | read | failed | deleted

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("recipient_id")]
    public string RecipientId { get; set; } = string.Empty;

    [JsonPropertyName("conversation")]
    public MetaConversationInfo? Conversation { get; set; }

    [JsonPropertyName("pricing")]
    public MetaPricingInfo? Pricing { get; set; }

    [JsonPropertyName("errors")]
    public List<MetaWebhookError>? Errors { get; set; }
}

public class MetaConversationInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("origin")]
    public MetaConversationOrigin? Origin { get; set; }

    [JsonPropertyName("expiration_timestamp")]
    public string? ExpirationTimestamp { get; set; }
}

public class MetaConversationOrigin
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    // business_initiated | user_initiated | referral_conversion
}

public class MetaPricingInfo
{
    [JsonPropertyName("billable")]
    public bool Billable { get; set; }

    [JsonPropertyName("pricing_model")]
    public string? PricingModel { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}

// ── Error ─────────────────────────────────────────────────────

public class MetaWebhookError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error_data")]
    public MetaErrorData? ErrorData { get; set; }
}

public class MetaErrorData
{
    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

// ── Account / system event models ────────────────────────────

public class MetaBanInfo
{
    [JsonPropertyName("waba_ban_state")]
    public string? WabaBanState { get; set; }  // SCHEDULE_FOR_DISABLE | DISABLE | REINSTATE

    [JsonPropertyName("waba_ban_date")]
    public string? WabaBanDate { get; set; }
}

public class MetaRestrictionInfo
{
    [JsonPropertyName("restriction_type")]
    public string? RestrictionType { get; set; }  // RESTRICTED_BIZ_INITIATED_MESSAGING etc.

    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }
}

public class MetaViolation
{
    [JsonPropertyName("violation_type")]
    public string? ViolationType { get; set; }
}
