// ============================================================
//  ChatRelay — IApiKeyService + ApiKeyService
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChatRelay.API.Services;

public interface IApiKeyService
{
    Task<ServiceResult<ApiKeyResponse>> CreateAsync(
        Guid tenantId, CreateApiKeyRequest request);

    Task<ServiceResult<List<ApiKeyResponse>>> GetAllAsync(
        Guid tenantId, bool isSuperAdmin);

    Task<ServiceResult<ApiKeyResponse>> GetByIdAsync(
        Guid keyId, Guid tenantId, bool isSuperAdmin);

    Task<ServiceResult<ApiKeyResponse>> UpdateAsync(
        Guid keyId, Guid tenantId, bool isSuperAdmin,
        UpdateApiKeyRequest request);

    Task<ServiceResult<bool>> RevokeAsync(
        Guid keyId, Guid tenantId, bool isSuperAdmin,
        RevokeApiKeyRequest request);

    // Called by ApiKeyMiddleware to validate incoming X-Api-Key
    Task<ApiKeyValidationResult?> ValidateKeyAsync(string rawKey);
}

public class ApiKeyService : IApiKeyService
{
    private readonly ApplicationDbContext _db;

    public ApiKeyService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ── Create ────────────────────────────────────────────────

    public async Task<ServiceResult<ApiKeyResponse>> CreateAsync(
        Guid tenantId, CreateApiKeyRequest request)
    {
        // Validate WABA belongs to tenant if scoped
        if (request.WabaId.HasValue)
        {
            var wabaExists = await _db.WabaAccounts.AnyAsync(w =>
                w.Id == request.WabaId.Value &&
                w.TenantId == tenantId);

            if (!wabaExists)
                return ServiceResult<ApiKeyResponse>.Fail(
                    "WABA not found or does not belong to your account");
        }

        // Validate scopes
        var invalidScopes = request.Scopes
            .Where(s => !ApiKeyScopes.All.Contains(s))
            .ToList();

        if (invalidScopes.Any())
            return ServiceResult<ApiKeyResponse>.Fail(
                $"Invalid scopes: {string.Join(", ", invalidScopes)}. " +
                $"Valid scopes: {string.Join(", ", ApiKeyScopes.All)}");

        // Check key limit per tenant (max 20)
        var keyCount = await _db.ApiKeys
            .CountAsync(k => k.TenantId == tenantId && k.IsActive);

        if (keyCount >= 20)
            return ServiceResult<ApiKeyResponse>.Fail(
                "Maximum of 20 active API keys allowed per tenant");

        // Generate the raw key — shown to user once
        // Format: cr_live_<32 random chars>
        var rawKey    = GenerateRawKey();
        var keyHash   = HashKey(rawKey);
        var keyPrefix = rawKey[..16]; // first 16 chars for display

        var apiKey = new ApiKey
        {
            Id                 = Guid.NewGuid(),
            TenantId           = tenantId,
            WabaId             = request.WabaId,
            KeyName            = request.KeyName.Trim(),
            KeyHash            = keyHash,
            KeyPrefix          = keyPrefix,
            Scopes             = JsonSerializer.Serialize(request.Scopes),
            RateLimitPerMinute = request.RateLimitPerMinute,
            RateLimitPerDay    = request.RateLimitPerDay,
            ExpiresAt          = request.ExpiresAt,
            IsActive           = true,
            TotalRequests      = 0,
            CreatedAt          = DateTime.UtcNow,
            UpdatedAt          = DateTime.UtcNow,
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync();

        // Load WABA name for response
        string? wabaName = null;
        if (request.WabaId.HasValue)
        {
            var waba = await _db.WabaAccounts.FindAsync(request.WabaId.Value);
            wabaName = waba?.DisplayName;
        }

        var response = ToResponse(apiKey, wabaName);
        // Return full key ONCE — it's the raw key before hashing
        response.FullKey = rawKey;

        return ServiceResult<ApiKeyResponse>.Ok(response);
    }

    // ── Get All ───────────────────────────────────────────────

    public async Task<ServiceResult<List<ApiKeyResponse>>> GetAllAsync(
        Guid tenantId, bool isSuperAdmin)
    {
        var query = _db.ApiKeys
            .Include(k => k.WabaAccount)
            .AsQueryable();

        if (!isSuperAdmin)
            query = query.Where(k => k.TenantId == tenantId);

        var keys = await query
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();

        var result = keys.Select(k =>
            ToResponse(k, k.WabaAccount?.DisplayName)
        ).ToList();

        return ServiceResult<List<ApiKeyResponse>>.Ok(result);
    }

    // ── Get By Id ─────────────────────────────────────────────

    public async Task<ServiceResult<ApiKeyResponse>> GetByIdAsync(
        Guid keyId, Guid tenantId, bool isSuperAdmin)
    {
        var key = await _db.ApiKeys
            .Include(k => k.WabaAccount)
            .FirstOrDefaultAsync(k => k.Id == keyId);

        if (key == null)
            return ServiceResult<ApiKeyResponse>.Fail("API key not found");

        if (!isSuperAdmin && key.TenantId != tenantId)
            return ServiceResult<ApiKeyResponse>.Fail("API key not found");

        return ServiceResult<ApiKeyResponse>.Ok(
            ToResponse(key, key.WabaAccount?.DisplayName));
    }

    // ── Update ────────────────────────────────────────────────

    public async Task<ServiceResult<ApiKeyResponse>> UpdateAsync(
        Guid keyId, Guid tenantId, bool isSuperAdmin,
        UpdateApiKeyRequest request)
    {
        var key = await _db.ApiKeys
            .Include(k => k.WabaAccount)
            .FirstOrDefaultAsync(k => k.Id == keyId);

        if (key == null)
            return ServiceResult<ApiKeyResponse>.Fail("API key not found");

        if (!isSuperAdmin && key.TenantId != tenantId)
            return ServiceResult<ApiKeyResponse>.Fail("API key not found");

        if (!key.IsActive)
            return ServiceResult<ApiKeyResponse>.Fail(
                "Cannot update a revoked API key");

        if (!string.IsNullOrWhiteSpace(request.KeyName))
            key.KeyName = request.KeyName.Trim();

        if (request.Scopes != null)
        {
            var invalid = request.Scopes
                .Where(s => !ApiKeyScopes.All.Contains(s)).ToList();
            if (invalid.Any())
                return ServiceResult<ApiKeyResponse>.Fail(
                    $"Invalid scopes: {string.Join(", ", invalid)}");
            key.Scopes = JsonSerializer.Serialize(request.Scopes);
        }

        if (request.RateLimitPerMinute.HasValue)
            key.RateLimitPerMinute = request.RateLimitPerMinute;

        if (request.RateLimitPerDay.HasValue)
            key.RateLimitPerDay = request.RateLimitPerDay;

        if (request.ExpiresAt.HasValue)
            key.ExpiresAt = request.ExpiresAt;

        key.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult<ApiKeyResponse>.Ok(
            ToResponse(key, key.WabaAccount?.DisplayName));
    }

    // ── Revoke ────────────────────────────────────────────────

    public async Task<ServiceResult<bool>> RevokeAsync(
        Guid keyId, Guid tenantId, bool isSuperAdmin,
        RevokeApiKeyRequest request)
    {
        var key = await _db.ApiKeys.FindAsync(keyId);

        if (key == null)
            return ServiceResult<bool>.Fail("API key not found");

        if (!isSuperAdmin && key.TenantId != tenantId)
            return ServiceResult<bool>.Fail("API key not found");

        if (!key.IsActive)
            return ServiceResult<bool>.Fail("API key is already revoked");

        key.IsActive      = false;
        key.RevokedAt     = DateTime.UtcNow;
        key.RevokedReason = request.Reason;
        key.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(true);
    }

    // ── Validate (used by middleware) ─────────────────────────

    public async Task<ApiKeyValidationResult?> ValidateKeyAsync(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey)) return null;

        var keyHash = HashKey(rawKey);

        var key = await _db.ApiKeys
            .Include(k => k.Tenant)
            .FirstOrDefaultAsync(k =>
                k.KeyHash == keyHash &&
                k.IsActive);

        if (key == null) return null;

        // Check expiry
        if (key.ExpiresAt.HasValue && key.ExpiresAt < DateTime.UtcNow)
            return null;

        // Check tenant is active
        if (!key.Tenant.IsActive) return null;

        // Update usage stats (fire-and-forget style — don't await)
        key.LastUsedAt    = DateTime.UtcNow;
        key.TotalRequests++;
        await _db.SaveChangesAsync();

        return new ApiKeyValidationResult
        {
            TenantId           = key.TenantId,
            WabaId             = key.WabaId,
            Scopes             = DeserializeScopes(key.Scopes),
            RateLimitPerMinute = key.RateLimitPerMinute ?? key.Tenant.ApiRateLimitPerMinute,
            RateLimitPerDay    = key.RateLimitPerDay,
            ApiKeyId           = key.Id,
        };
    }

    // ── Helpers ───────────────────────────────────────────────

    private static string GenerateRawKey()
    {
        var bytes  = RandomNumberGenerator.GetBytes(32);;
        var random = Convert.ToBase64String(bytes)
            .Replace("+", "a").Replace("/", "b").Replace("=", "c");
        return $"cr_live_{random[..32]}";
    }

    private static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToBase64String(bytes);
    }

    private static List<string> DeserializeScopes(string scopesJson)
    {
        try { return JsonSerializer.Deserialize<List<string>>(scopesJson) ?? new(); }
        catch { return new(); }
    }

    private static ApiKeyResponse ToResponse(ApiKey k, string? wabaName) => new()
    {
        Id                 = k.Id,
        TenantId           = k.TenantId,
        WabaId             = k.WabaId,
        WabaDisplayName    = wabaName,
        KeyName            = k.KeyName,
        KeyPrefix          = k.KeyPrefix,
        Scopes             = DeserializeScopes(k.Scopes),
        RateLimitPerMinute = k.RateLimitPerMinute,
        RateLimitPerDay    = k.RateLimitPerDay,
        IsActive           = k.IsActive,
        TotalRequests      = k.TotalRequests,
        LastUsedAt         = k.LastUsedAt,
        LastUsedIp         = k.LastUsedIp,
        ExpiresAt          = k.ExpiresAt,
        RevokedAt          = k.RevokedAt,
        RevokedReason      = k.RevokedReason,
        CreatedAt          = k.CreatedAt,
    };
}

// ── Validation result returned to middleware ──────────────────

public class ApiKeyValidationResult
{
    public Guid TenantId { get; set; }
    public Guid? WabaId { get; set; }
    public Guid ApiKeyId { get; set; }
    public List<string> Scopes { get; set; } = new();
    public int RateLimitPerMinute { get; set; }
    public int? RateLimitPerDay { get; set; }

    public bool HasScope(string scope) =>
        Scopes.Contains(scope) || Scopes.Contains("*");
}
