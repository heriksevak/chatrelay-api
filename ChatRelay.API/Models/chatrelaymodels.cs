// ============================================================
//  ChatRelay — Complete EF Core Entity Models
//  Database: MySQL via Pomelo.EntityFrameworkCore.MySql
// ============================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChatRelay.Models;

// ─────────────────────────────────────────────
//  ENUMS
// ─────────────────────────────────────────────

public enum PlanType { Free, Starter, Growth, Enterprise, Custom }
public enum UserRole { SuperAdmin, TenantAdmin, TenantUser }
public enum WabaStatus { Pending, Active, Suspended, Disconnected }
public enum MessageStatus { Pending, Queued, Sent, Delivered, Read, Failed, Cancelled }
public enum MessageDirection { Outbound, Inbound }
public enum MessageType { Text, Image, Video, Audio, Document, Location, Contact, Template, Reaction, Sticker }
public enum ConversationStatus { Open, Resolved, Pending, Spam }
public enum TemplateCategory { Marketing, Utility, Authentication }
public enum TemplateStatus { Draft, Pending, Approved, Rejected, Paused }
public enum TemplateHeaderType { None, Text, Image, Video, Document }
public enum TriggerType { Keyword, AnyMessage, FirstMessage, OutsideHours }
public enum MatchType { Exact, Contains, StartsWith, Regex }
public enum ReplyType { Text, Template }
public enum WebhookEvent { MessageSent, MessageDelivered, MessageRead, MessageFailed, InboundMessage, ConversationOpened, ConversationResolved }


// ─────────────────────────────────────────────
//  BASE ENTITY
// ─────────────────────────────────────────────

public abstract class BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public abstract class SoftDeletableEntity : BaseEntity
{
    public DateTime? DeletedAt { get; set; }
    public bool IsDeleted => DeletedAt.HasValue;
}


// ─────────────────────────────────────────────
//  TENANTS
// ─────────────────────────────────────────────

[Table("Tenants")]
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(CustomDomain), IsUnique = true)]
public class Tenant : SoftDeletableEntity
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    // URL-safe unique identifier e.g. "acme-agency"
    [Required, MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    public PlanType PlanType { get; set; } = PlanType.Free;

    // Plan limits
    public int MaxWabas { get; set; } = 3;
    public int MaxUsersPerWaba { get; set; } = 5;
    public int MaxMessagesPerMonth { get; set; } = 1000;
    public int MaxTemplatesPerWaba { get; set; } = 25;
    public int MaxContactsPerWaba { get; set; } = 10000;
    public int ApiRateLimitPerMinute { get; set; } = 60;

    public bool IsActive { get; set; } = true;
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }

    // White-label branding
    public bool IsWhiteLabel { get; set; } = false;
    [MaxLength(200)] public string? BrandName { get; set; }
    [MaxLength(500)] public string? BrandLogoUrl { get; set; }
    [MaxLength(7)] public string? BrandPrimaryColor { get; set; }   // hex e.g. #1A73E8
    [MaxLength(7)] public string? BrandSecondaryColor { get; set; }
    [MaxLength(200)] public string? CustomDomain { get; set; }
    [MaxLength(500)] public string? FaviconUrl { get; set; }
    [MaxLength(500)] public string? SupportEmail { get; set; }
    [MaxLength(500)] public string? SupportUrl { get; set; }

    // Billing
    [MaxLength(200)] public string? BillingEmail { get; set; }
    [MaxLength(500)] public string? BillingAddress { get; set; }
    [MaxLength(100)] public string? StripeCustomerId { get; set; }
    [MaxLength(100)] public string? StripeSubscriptionId { get; set; }

    // Metadata
    [MaxLength(100)] public string? Country { get; set; }
    [MaxLength(50)] public string? Timezone { get; set; }
    [Column(TypeName = "json")] public string? Metadata { get; set; } // flexible KV store

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<WabaAccount> WabaAccounts { get; set; } = new List<WabaAccount>();
    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<WebhookEndpoint> WebhookEndpoints { get; set; } = new List<WebhookEndpoint>();
}


// ─────────────────────────────────────────────
//  USERS
// ─────────────────────────────────────────────

[Table("Users")]
[Index(nameof(Email), IsUnique = true)]
[Index(nameof(TenantId))]
public class User : SoftDeletableEntity
{
    public Guid TenantId { get; set; }

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.TenantUser;
    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; } = false;

    [MaxLength(500)] public string? AvatarUrl { get; set; }
    [MaxLength(50)] public string? PhoneNumber { get; set; }
    [MaxLength(100)] public string? Timezone { get; set; }
    [MaxLength(10)] public string? Language { get; set; }

    // Auth tokens
    [MaxLength(500)] public string? EmailVerifyToken { get; set; }
    public DateTime? EmailVerifyTokenExpiry { get; set; }
    [MaxLength(500)] public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiry { get; set; }
    [MaxLength(500)] public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }

    // 2FA
    public bool TwoFactorEnabled { get; set; } = false;
    [MaxLength(500)] public string? TwoFactorSecret { get; set; }

    public DateTime? LastLoginAt { get; set; }
    [MaxLength(100)] public string? LastLoginIp { get; set; }
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }

    // Navigation
    [ForeignKey(nameof(TenantId))]
    public Tenant Tenant { get; set; } = null!;
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}


// ─────────────────────────────────────────────
//  WABA ACCOUNTS
// ─────────────────────────────────────────────

[Table("WabaAccounts")]
[Index(nameof(TenantId))]
[Index(nameof(PhoneNumber))]
[Index(nameof(PhoneNumberId), IsUnique = true)]
public class WabaAccount : SoftDeletableEntity
{
    public Guid TenantId { get; set; }

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;  // e.g. +919876543210

    // Meta / WhatsApp identifiers
    [MaxLength(100)] public string? PhoneNumberId { get; set; }  // Meta phone number ID
    [MaxLength(100)] public string? WabaId { get; set; }         // WhatsApp Business Account ID
    [MaxLength(100)] public string? MetaBusinessId { get; set; }

    // Business profile
    [MaxLength(200)] public string? BusinessName { get; set; }
    [MaxLength(500)] public string? BusinessDescription { get; set; }
    [MaxLength(500)] public string? ProfilePictureUrl { get; set; }
    [MaxLength(200)] public string? BusinessWebsite { get; set; }
    [MaxLength(100)] public string? BusinessCategory { get; set; }
    [MaxLength(200)] public string? BusinessEmail { get; set; }
    [MaxLength(500)] public string? BusinessAddress { get; set; }

    // Encrypted credentials (encrypt at application layer before storing)
    [MaxLength(500)] public string? AiSensyApiKey { get; set; }
    [MaxLength(500)] public string? AiSensyCampaignName { get; set; }
    [MaxLength(1000)] public string? MetaAccessToken { get; set; }
    [MaxLength(500)] public string? MetaAppSecret { get; set; }
    [MaxLength(200)] public string? WebhookVerifyToken { get; set; }
    public DateTime? MetaTokenExpiresAt { get; set; }

    public WabaStatus Status { get; set; } = WabaStatus.Pending;
    public bool IsActive { get; set; } = true;

    // Limits
    public int DailyMessageLimit { get; set; } = 1000;
    public int MonthlyMessageLimit { get; set; } = 10000;
    public int CurrentMonthMessageCount { get; set; } = 0;
    public DateTime? LimitResetAt { get; set; }

    // Quality
    [MaxLength(50)] public string? QualityRating { get; set; }  // HIGH, MEDIUM, LOW
    [MaxLength(50)] public string? MessagingTier { get; set; }  // TIER_1, TIER_2 etc.

    // Timestamps
    public DateTime? LastMessageAt { get; set; }
    public DateTime? ConnectedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(TenantId))]
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Template> Templates { get; set; } = new List<Template>();
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
    public ICollection<AutoReplyRule> AutoReplyRules { get; set; } = new List<AutoReplyRule>();
    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
    public ICollection<WebhookEndpoint> WebhookEndpoints { get; set; } = new List<WebhookEndpoint>();
}


// ─────────────────────────────────────────────
//  CONTACTS
// ─────────────────────────────────────────────

[Table("Contacts")]
[Index(nameof(WabaId), nameof(PhoneNumber), IsUnique = true)]
[Index(nameof(WabaId))]
public class Contact : BaseEntity
{
    public Guid WabaId { get; set; }

    [Required, MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [MaxLength(200)] public string? Name { get; set; }
    [MaxLength(320)] public string? Email { get; set; }
    [MaxLength(500)] public string? AvatarUrl { get; set; }
    [MaxLength(100)] public string? Country { get; set; }
    [MaxLength(50)] public string? Language { get; set; }

    // Segmentation
    [Column(TypeName = "json")] public string? Tags { get; set; }           // ["vip","lead"]
    [Column(TypeName = "json")] public string? CustomFields { get; set; }   // {"plan":"gold"}

    // Consent
    public bool IsBlocked { get; set; } = false;
    public bool IsOptedOut { get; set; } = false;
    public DateTime? OptedOutAt { get; set; }
    public bool IsOptedIn { get; set; } = true;
    public DateTime? OptedInAt { get; set; }

    // Stats
    public int TotalMessagesSent { get; set; } = 0;
    public int TotalMessagesReceived { get; set; } = 0;
    public DateTime? LastMessageAt { get; set; }
    public DateTime? FirstMessageAt { get; set; }

    // Navigation
    [ForeignKey(nameof(WabaId))]
    public WabaAccount WabaAccount { get; set; } = null!;
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}


// ─────────────────────────────────────────────
//  CONVERSATIONS
// ─────────────────────────────────────────────

[Table("Conversations")]
[Index(nameof(WabaId))]
[Index(nameof(ContactId))]
[Index(nameof(AssignedToUserId))]
[Index(nameof(Status))]
public class Conversation : BaseEntity
{
    public Guid WabaId { get; set; }
    public Guid ContactId { get; set; }
    public Guid? AssignedToUserId { get; set; }

    public ConversationStatus Status { get; set; } = ConversationStatus.Open;

    [MaxLength(50)] public string Channel { get; set; } = "whatsapp";  // whatsapp, sms

    // 24-hour session window tracking
    public DateTime? SessionExpiresAt { get; set; }
    public bool IsSessionOpen => SessionExpiresAt.HasValue && SessionExpiresAt > DateTime.UtcNow;

    // Stats
    public int MessageCount { get; set; } = 0;
    public int UnreadCount { get; set; } = 0;
    public DateTime? LastMessageAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    [MaxLength(500)] public string? Notes { get; set; }
    [Column(TypeName = "json")] public string? Tags { get; set; }

    // Navigation
    [ForeignKey(nameof(WabaId))]
    public WabaAccount WabaAccount { get; set; } = null!;
    [ForeignKey(nameof(ContactId))]
    public Contact Contact { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}


// ─────────────────────────────────────────────
//  MESSAGES
// ─────────────────────────────────────────────

[Table("Messages")]
[Index(nameof(WabaId))]
[Index(nameof(ConversationId))]
[Index(nameof(ContactId))]
[Index(nameof(Status))]
[Index(nameof(ProviderMessageId))]
[Index(nameof(CreatedAt))]
public class Message : BaseEntity
{
    public Guid WabaId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ContactId { get; set; }

    public MessageDirection Direction { get; set; } = MessageDirection.Outbound;
    public MessageType MessageType { get; set; } = MessageType.Text;
    public MessageStatus Status { get; set; } = MessageStatus.Pending;

    [MaxLength(4096)] public string? Content { get; set; }

    // Media
    [MaxLength(1000)] public string? MediaUrl { get; set; }
    [MaxLength(100)] public string? MediaMimeType { get; set; }
    [MaxLength(500)] public string? MediaCaption { get; set; }
    public int? MediaSizeBytes { get; set; }
    [MaxLength(100)] public string? MetaMediaId { get; set; }

    // Template message fields
    public Guid? TemplateId { get; set; }
    [Column(TypeName = "json")] public string? TemplateParams { get; set; }

    // Reply context
    [MaxLength(200)] public string? ReplyToMessageId { get; set; }  // Meta message ID

    // Provider tracking
    [MaxLength(200)] public string? ProviderMessageId { get; set; }  // AiSensy / Meta message ID
    [MaxLength(100)] public string? Provider { get; set; }           // "aisensy", "meta", "mock"

    // Failure handling
    [MaxLength(500)] public string? FailureReason { get; set; }
    [MaxLength(100)] public string? FailureCode { get; set; }
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
    public DateTime? NextRetryAt { get; set; }

    // Scheduling
    public DateTime? ScheduledAt { get; set; }

    // Status timestamps
    public DateTime? QueuedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Who sent it (for outbound)
    public Guid? SentByUserId { get; set; }
    public bool IsAutomated { get; set; } = false;
    public Guid? AutoReplyRuleId { get; set; }

    // Context
    [Column(TypeName = "json")] public string? RawPayload { get; set; }  // store raw webhook JSON for debugging

    // Navigation
    [ForeignKey(nameof(WabaId))]
    public WabaAccount WabaAccount { get; set; } = null!;
    [ForeignKey(nameof(ConversationId))]
    public Conversation Conversation { get; set; } = null!;
    [ForeignKey(nameof(ContactId))]
    public Contact Contact { get; set; } = null!;
}


// ─────────────────────────────────────────────
//  TEMPLATES
// ─────────────────────────────────────────────

[Table("Templates")]
[Index(nameof(WabaId))]
[Index(nameof(MetaTemplateId))]
[Index(nameof(Status))]
public class Template : BaseEntity
{
    public Guid WabaId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;  // must be lowercase_underscore for Meta

    public TemplateCategory Category { get; set; } = TemplateCategory.Utility;
    [MaxLength(10)] public string Language { get; set; } = "en";
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;

    // Header
    public TemplateHeaderType HeaderType { get; set; } = TemplateHeaderType.None;
    [MaxLength(1000)] public string? HeaderContent { get; set; }

    // Body (supports {{1}}, {{2}} variable placeholders)
    [Required, MaxLength(4096)]
    public string BodyText { get; set; } = string.Empty;

    // Footer
    [MaxLength(200)] public string? FooterText { get; set; }

    // Buttons stored as JSON array
    [Column(TypeName = "json")] public string? ButtonsJson { get; set; }

    // Meta sync
    [MaxLength(100)] public string? MetaTemplateId { get; set; }
    [MaxLength(500)] public string? RejectionReason { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }

    // Usage stats
    public int TimesUsed { get; set; } = 0;
    public DateTime? LastUsedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(WabaId))]
    public WabaAccount WabaAccount { get; set; } = null!;
}


// ─────────────────────────────────────────────
//  MEDIA FILES
// ─────────────────────────────────────────────

[Table("MediaFiles")]
[Index(nameof(WabaId))]
[Index(nameof(MetaMediaId))]
public class MediaFile : BaseEntity
{
    public Guid WabaId { get; set; }
    public Guid? UploadedByUserId { get; set; }

    [Required, MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(50)] public string? FileExtension { get; set; }
    [MaxLength(100)] public string? MimeType { get; set; }
    public long FileSizeBytes { get; set; }

    // Storage
    [MaxLength(1000)] public string? StorageUrl { get; set; }     // your own CDN/blob URL
    [MaxLength(200)] public string? StoragePath { get; set; }    // relative path in storage

    // Meta media caching (Meta media URLs expire after 5 mins but IDs are reusable)
    [MaxLength(200)] public string? MetaMediaId { get; set; }
    public DateTime? MetaMediaExpiresAt { get; set; }

    // Navigation
    [ForeignKey(nameof(WabaId))]
    public WabaAccount WabaAccount { get; set; } = null!;
}


// ─────────────────────────────────────────────
//  AUTO REPLY RULES
// ─────────────────────────────────────────────

[Table("AutoReplyRules")]
[Index(nameof(WabaId))]
[Index(nameof(IsActive))]
public class AutoReplyRule : BaseEntity
{
    public Guid WabaId { get; set; }

    [Required, MaxLength(200)]
    public string RuleName { get; set; } = string.Empty;

    public TriggerType TriggerType { get; set; } = TriggerType.Keyword;

    // Keywords (comma-separated or JSON array)
    [Column(TypeName = "json")] public string? Keywords { get; set; }
    public MatchType MatchType { get; set; } = MatchType.Contains;

    // Reply
    public ReplyType ReplyType { get; set; } = ReplyType.Text;
    [MaxLength(4096)] public string? ReplyContent { get; set; }
    public Guid? TemplateId { get; set; }
    [Column(TypeName = "json")] public string? TemplateParams { get; set; }

    // Control
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;       // lower = higher priority

    // Schedule (only trigger during these hours)
    public TimeOnly? ActiveFromTime { get; set; }
    public TimeOnly? ActiveUntilTime { get; set; }
    [MaxLength(50)] public string? ActiveTimezone { get; set; }

    // Days of week (bitmask: 1=Mon ... 64=Sun)
    public int? ActiveDaysBitmask { get; set; }

    // Throttle — don't reply to same contact more than once per X minutes
    public int? ThrottleMinutes { get; set; }

    // Stats
    public int TriggerCount { get; set; } = 0;
    public DateTime? LastTriggeredAt { get; set; }

    // Navigation
    [ForeignKey(nameof(WabaId))]
    public WabaAccount WabaAccount { get; set; } = null!;
    [ForeignKey(nameof(TemplateId))]
    public Template? Template { get; set; }
}


// ─────────────────────────────────────────────
//  API KEYS
// ─────────────────────────────────────────────

[Table("ApiKeys")]
[Index(nameof(KeyHash), IsUnique = true)]
[Index(nameof(TenantId))]
[Index(nameof(WabaId))]
public class ApiKey : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid? WabaId { get; set; }  // null = tenant-level key (all WABAs)

    [Required, MaxLength(200)]
    public string KeyName { get; set; } = string.Empty;

    // Store SHA256 hash of actual key, never the key itself
    [Required, MaxLength(500)]
    public string KeyHash { get; set; } = string.Empty;

    // First 8 chars for display e.g. "cr_live_ab12cd34..."
    [MaxLength(20)]
    public string KeyPrefix { get; set; } = string.Empty;

    // Scopes as JSON array e.g. ["messages:send","contacts:read"]
    [Column(TypeName = "json")]
    public string Scopes { get; set; } = "[]";

    // Rate limits (override tenant defaults)
    public int? RateLimitPerMinute { get; set; }
    public int? RateLimitPerDay { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    [MaxLength(100)] public string? LastUsedIp { get; set; }
    public long TotalRequests { get; set; } = 0;

    public DateTime? RevokedAt { get; set; }
    [MaxLength(200)] public string? RevokedReason { get; set; }

    // Navigation
    [ForeignKey(nameof(TenantId))]
    public Tenant Tenant { get; set; } = null!;
    [ForeignKey(nameof(WabaId))]
    public WabaAccount? WabaAccount { get; set; }
}


// ─────────────────────────────────────────────
//  WEBHOOK ENDPOINTS (outbound — notify tenant's systems)
// ─────────────────────────────────────────────

[Table("WebhookEndpoints")]
[Index(nameof(TenantId))]
[Index(nameof(WabaId))]
public class WebhookEndpoint : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid? WabaId { get; set; }  // null = all WABAs for this tenant

    [Required, MaxLength(1000)]
    public string Url { get; set; } = string.Empty;

    // Events as JSON array e.g. ["MessageDelivered","InboundMessage"]
    [Column(TypeName = "json")]
    public string Events { get; set; } = "[]";

    // HMAC-SHA256 secret for signature header
    [MaxLength(500)] public string? SecretHash { get; set; }

    public bool IsActive { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;

    // Health stats
    public int TotalDeliveries { get; set; } = 0;
    public int FailedDeliveries { get; set; } = 0;
    public int ConsecutiveFailures { get; set; } = 0;
    public DateTime? LastDeliveryAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    [MaxLength(500)] public string? LastFailureReason { get; set; }
    public bool IsDisabledDueToFailures { get; set; } = false;

    // Navigation
    [ForeignKey(nameof(TenantId))]
    public Tenant Tenant { get; set; } = null!;
    [ForeignKey(nameof(WabaId))]
    public WabaAccount? WabaAccount { get; set; }
}


// ─────────────────────────────────────────────
//  AUDIT LOGS
// ─────────────────────────────────────────────

[Table("AuditLogs")]
[Index(nameof(TenantId))]
[Index(nameof(UserId))]
[Index(nameof(EntityType), nameof(EntityId))]
[Index(nameof(CreatedAt))]
public class AuditLog
{
    [Key]
    public long Id { get; set; }  // long for high-volume append-only table

    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? WabaId { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty;  // e.g. "waba.created", "message.sent"

    [MaxLength(100)] public string? EntityType { get; set; }  // "WabaAccount", "Message"
    [MaxLength(100)] public string? EntityId { get; set; }

    [Column(TypeName = "json")] public string? OldValues { get; set; }
    [Column(TypeName = "json")] public string? NewValues { get; set; }

    [MaxLength(100)] public string? IpAddress { get; set; }
    [MaxLength(500)] public string? UserAgent { get; set; }
    [MaxLength(200)] public string? RequestId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(TenantId))]
    public Tenant Tenant { get; set; } = null!;
}
public class Enquiry
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [Phone]
    public string Phone { get; set; }

    [MaxLength(150)]
    public string BusinessName { get; set; }

    [MaxLength(1000)]
    public string Message { get; set; }

    public string Status { get; set; } = "New";

    public string Source { get; set; } = "Website";
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}