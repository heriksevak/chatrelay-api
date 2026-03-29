// ============================================================
//  ChatRelay — ApiKey DTOs
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace ChatRelay.API.DTOs;

public class CreateApiKeyRequest
{
    [Required, MaxLength(200)]
    public string KeyName { get; set; } = string.Empty;

    // null = tenant-level (all WABAs), set = scoped to one WABA
    public Guid? WabaId { get; set; }

    // Scopes e.g. ["messages:send","contacts:read","templates:read"]
    public List<string> Scopes { get; set; } = new() { "messages:send", "messages:read" };

    // Rate limits — null means use tenant defaults
    public int? RateLimitPerMinute { get; set; }
    public int? RateLimitPerDay { get; set; }

    // Optional expiry — null means never expires
    public DateTime? ExpiresAt { get; set; }
}

public class ApiKeyResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? WabaId { get; set; }
    public string? WabaDisplayName { get; set; }
    public string KeyName { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;

    // Full key — only populated on creation
    public string? FullKey { get; set; }

    public List<string> Scopes { get; set; } = new();
    public int? RateLimitPerMinute { get; set; }
    public int? RateLimitPerDay { get; set; }
    public bool IsActive { get; set; }
    public long TotalRequests { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RevokeApiKeyRequest
{
    [MaxLength(200)]
    public string? Reason { get; set; }
}

public class UpdateApiKeyRequest
{
    [MaxLength(200)]
    public string? KeyName { get; set; }
    public List<string>? Scopes { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public int? RateLimitPerDay { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

// Available scopes reference
public static class ApiKeyScopes
{
    public const string MessagesSend    = "messages:send";
    public const string MessagesRead    = "messages:read";
    public const string ContactsRead    = "contacts:read";
    public const string ContactsWrite   = "contacts:write";
    public const string TemplatesRead   = "templates:read";
    public const string TemplatesWrite  = "templates:write";
    public const string WebhooksRead    = "webhooks:read";
    public const string WebhooksWrite   = "webhooks:write";
    public const string WabasRead       = "wabas:read";
    public const string AnalyticsRead   = "analytics:read";

    public static readonly List<string> All = new()
    {
        MessagesSend, MessagesRead, ContactsRead, ContactsWrite,
        TemplatesRead, TemplatesWrite, WebhooksRead, WebhooksWrite,
        WabasRead, AnalyticsRead
    };
}
