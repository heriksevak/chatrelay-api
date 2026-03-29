// ============================================================
//  ChatRelay — IWabaService + WabaService
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatRelay.API.Services;

public interface IWabaService
{
    Task<ServiceResult<WabaResponse>> OnboardViaFacebookAsync(
        WabaOnboardRequest request, Guid tenantId);

    Task<ServiceResult<WabaResponse>> OnboardManualAsync(
        WabaManualRequest request, Guid tenantId);

    Task<ServiceResult<List<WabaResponse>>> GetWabasAsync(
        Guid tenantId, bool isSuperAdmin);

    Task<ServiceResult<WabaResponse>> GetWabaByIdAsync(
        Guid wabaId, Guid tenantId, bool isSuperAdmin);

    Task<ServiceResult<WabaResponse>> UpdateWabaAsync(
        Guid wabaId, Guid tenantId, bool isSuperAdmin,
        UpdateWabaRequest request);

    Task<ServiceResult<bool>> DisconnectWabaAsync(
        Guid wabaId, Guid tenantId, bool isSuperAdmin);

    Task<ServiceResult<CredentialValidationResult>> ValidateCredentialsAsync(
        Guid wabaId, Guid tenantId, bool isSuperAdmin);
}

public class WabaService : IWabaService
{
    private readonly ApplicationDbContext _db;
    private readonly IMetaGraphService _metaGraph;
    private readonly IAiSensyValidationService _aiSensyValidator;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<WabaService> _logger;

    public WabaService(
        ApplicationDbContext db,
        IMetaGraphService metaGraph,
        IAiSensyValidationService aiSensyValidator,
        IEncryptionService encryption,
        ILogger<WabaService> logger)
    {
        _db               = db;
        _metaGraph        = metaGraph;
        _aiSensyValidator = aiSensyValidator;
        _encryption       = encryption;
        _logger           = logger;
    }

    // ── Onboard via Facebook Embedded Signup ─────────────────

    public async Task<ServiceResult<WabaResponse>> OnboardViaFacebookAsync(
        WabaOnboardRequest request, Guid tenantId)
    {
        // 1. Check tenant WABA limit
        var limitCheck = await CheckWabaLimitAsync(tenantId);
        if (!limitCheck.Success) return limitCheck;

        // 2. Check phone number not already registered
        var phoneExists = await _db.WabaAccounts
            .AnyAsync(w => w.PhoneNumberId == request.PhoneNumberId);
        if (phoneExists)
            return ServiceResult<WabaResponse>.Fail(
                "This WhatsApp number is already connected to another account");

        // 3. Validate AiSensy key first (fail fast before calling Meta)
        var aiSensyResult = await _aiSensyValidator.ValidateApiKeyAsync(request.AiSensyApiKey);
        if (!aiSensyResult.IsValid)
            return ServiceResult<WabaResponse>.Fail(
                $"AiSensy validation failed: {aiSensyResult.Error}");

        // 4. Exchange Facebook code for Meta access token
        MetaTokenResult tokenResult;
        try
        {
            tokenResult = await _metaGraph.ExchangeCodeForTokenAsync(request.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Meta code exchange failed for tenant {TenantId}", tenantId);
            return ServiceResult<WabaResponse>.Fail(
                "Failed to connect with Facebook. Please try again.");
        }

        // 5. Fetch WABA profile from Meta
        MetaWabaProfile wabaProfile;
        MetaPhoneNumberProfile phoneProfile;
        try
        {
            wabaProfile   = await _metaGraph.GetWabaProfileAsync(
                request.WabaId, tokenResult.AccessToken);
            phoneProfile  = await _metaGraph.GetPhoneNumberProfileAsync(
                request.PhoneNumberId, tokenResult.AccessToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Meta profile fetch failed for WABA {WabaId}", request.WabaId);
            return ServiceResult<WabaResponse>.Fail(
                "Connected to Facebook but could not fetch WhatsApp profile. " +
                "Check your WABA ID and Phone Number ID.");
        }

        // 6. Build and save WABA record with encrypted credentials
        var waba = new WabaAccount
        {
            Id             = Guid.NewGuid(),
            TenantId       = tenantId,
            DisplayName    = request.DisplayName
                             ?? phoneProfile.VerifiedName
                             ?? wabaProfile.Name,
            PhoneNumber    = phoneProfile.DisplayPhoneNumber,
            PhoneNumberId  = request.PhoneNumberId,
            WabaId         = request.WabaId,
            BusinessName   = wabaProfile.Name,
            QualityRating  = phoneProfile.QualityRating,
            MessagingTier  = phoneProfile.MessagingLimitTier,

            // Encrypt sensitive credentials before storing
            AiSensyApiKey    = _encryption.Encrypt(request.AiSensyApiKey),
            MetaAccessToken  = _encryption.Encrypt(tokenResult.AccessToken),
            MetaTokenExpiresAt = tokenResult.ExpiresAt,

            // Generate a unique webhook verify token for this WABA
            WebhookVerifyToken = GenerateWebhookVerifyToken(),

            Status      = WabaStatus.Active,
            IsActive    = true,
            ConnectedAt = DateTime.UtcNow,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        _db.WabaAccounts.Add(waba);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "WABA {WabaId} onboarded for tenant {TenantId} via Facebook SDK",
            waba.Id, tenantId);

        return ServiceResult<WabaResponse>.Ok(ToResponse(waba));
    }

    // ── Onboard manually (fallback) ───────────────────────────

    public async Task<ServiceResult<WabaResponse>> OnboardManualAsync(
        WabaManualRequest request, Guid tenantId)
    {
        var limitCheck = await CheckWabaLimitAsync(tenantId);
        if (!limitCheck.Success) return limitCheck;

        var phoneExists = await _db.WabaAccounts
            .AnyAsync(w => w.PhoneNumberId == request.PhoneNumberId);
        if (phoneExists)
            return ServiceResult<WabaResponse>.Fail(
                "This WhatsApp number is already connected");

        // Validate AiSensy key
        var aiSensyResult = await _aiSensyValidator.ValidateApiKeyAsync(request.AiSensyApiKey);
        if (!aiSensyResult.IsValid)
            return ServiceResult<WabaResponse>.Fail(
                $"AiSensy validation failed: {aiSensyResult.Error}");

        // Validate Meta token
        var metaValid = await _metaGraph.ValidateTokenAsync(request.MetaAccessToken);
        if (!metaValid)
            return ServiceResult<WabaResponse>.Fail(
                "Meta access token is invalid or expired");

        var waba = new WabaAccount
        {
            Id                 = Guid.NewGuid(),
            TenantId           = tenantId,
            DisplayName        = request.DisplayName,
            PhoneNumber        = request.PhoneNumber,
            PhoneNumberId      = request.PhoneNumberId,
            WabaId             = request.WabaId,
            AiSensyApiKey      = _encryption.Encrypt(request.AiSensyApiKey),
            MetaAccessToken    = _encryption.Encrypt(request.MetaAccessToken),
            MetaAppSecret      = _encryption.Encrypt(request.MetaAppSecret),
            WebhookVerifyToken = GenerateWebhookVerifyToken(),
            Status             = WabaStatus.Active,
            IsActive           = true,
            ConnectedAt        = DateTime.UtcNow,
            CreatedAt          = DateTime.UtcNow,
            UpdatedAt          = DateTime.UtcNow,
        };

        _db.WabaAccounts.Add(waba);
        await _db.SaveChangesAsync();

        return ServiceResult<WabaResponse>.Ok(ToResponse(waba));
    }

    // ── Get WABAs ─────────────────────────────────────────────

    public async Task<ServiceResult<List<WabaResponse>>> GetWabasAsync(
        Guid tenantId, bool isSuperAdmin)
    {
        var query = _db.WabaAccounts.AsQueryable();

        if (!isSuperAdmin)
            query = query.Where(w => w.TenantId == tenantId);

        var wabas = await query
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();

        return ServiceResult<List<WabaResponse>>.Ok(
            wabas.Select(ToResponse).ToList());
    }

    // ── Get single WABA ───────────────────────────────────────

    public async Task<ServiceResult<WabaResponse>> GetWabaByIdAsync(
        Guid wabaId, Guid tenantId, bool isSuperAdmin)
    {
        var waba = await _db.WabaAccounts.FindAsync(wabaId);

        if (waba == null)
            return ServiceResult<WabaResponse>.Fail("WABA not found");

        if (!isSuperAdmin && waba.TenantId != tenantId)
            return ServiceResult<WabaResponse>.Fail("WABA not found");

        return ServiceResult<WabaResponse>.Ok(ToResponse(waba));
    }

    // ── Update WABA ───────────────────────────────────────────

    public async Task<ServiceResult<WabaResponse>> UpdateWabaAsync(
        Guid wabaId, Guid tenantId, bool isSuperAdmin,
        UpdateWabaRequest request)
    {
        var waba = await _db.WabaAccounts.FindAsync(wabaId);

        if (waba == null)
            return ServiceResult<WabaResponse>.Fail("WABA not found");

        if (!isSuperAdmin && waba.TenantId != tenantId)
            return ServiceResult<WabaResponse>.Fail("WABA not found");

        // If updating AiSensy key, validate it first
        if (!string.IsNullOrWhiteSpace(request.AiSensyApiKey))
        {
            var aiSensyResult = await _aiSensyValidator
                .ValidateApiKeyAsync(request.AiSensyApiKey);
            if (!aiSensyResult.IsValid)
                return ServiceResult<WabaResponse>.Fail(
                    $"AiSensy validation failed: {aiSensyResult.Error}");

            waba.AiSensyApiKey = _encryption.Encrypt(request.AiSensyApiKey);
        }

        if (request.DisplayName != null)
            waba.DisplayName = request.DisplayName;
        if (request.BusinessName != null)
            waba.BusinessName = request.BusinessName;
        if (request.BusinessDescription != null)
            waba.BusinessDescription = request.BusinessDescription;
        if (request.BusinessWebsite != null)
            waba.BusinessWebsite = request.BusinessWebsite;
        if (request.BusinessCategory != null)
            waba.BusinessCategory = request.BusinessCategory;
        if (request.BusinessEmail != null)
            waba.BusinessEmail = request.BusinessEmail;
        if (request.DailyMessageLimit.HasValue)
            waba.DailyMessageLimit = request.DailyMessageLimit.Value;
        if (request.MonthlyMessageLimit.HasValue)
            waba.MonthlyMessageLimit = request.MonthlyMessageLimit.Value;

        waba.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult<WabaResponse>.Ok(ToResponse(waba));
    }

    // ── Disconnect WABA ───────────────────────────────────────

    public async Task<ServiceResult<bool>> DisconnectWabaAsync(
        Guid wabaId, Guid tenantId, bool isSuperAdmin)
    {
        var waba = await _db.WabaAccounts.FindAsync(wabaId);

        if (waba == null)
            return ServiceResult<bool>.Fail("WABA not found");

        if (!isSuperAdmin && waba.TenantId != tenantId)
            return ServiceResult<bool>.Fail("WABA not found");

        waba.IsActive  = false;
        waba.Status    = WabaStatus.Disconnected;
        waba.DeletedAt = DateTime.UtcNow;
        waba.UpdatedAt = DateTime.UtcNow;

        // Clear credentials on disconnect
        waba.MetaAccessToken = null;
        waba.AiSensyApiKey   = null;

        await _db.SaveChangesAsync();

        return ServiceResult<bool>.Ok(true);
    }

    // ── Validate credentials ──────────────────────────────────

    public async Task<ServiceResult<CredentialValidationResult>> ValidateCredentialsAsync(
        Guid wabaId, Guid tenantId, bool isSuperAdmin)
    {
        var waba = await _db.WabaAccounts.FindAsync(wabaId);

        if (waba == null)
            return ServiceResult<CredentialValidationResult>.Fail("WABA not found");

        if (!isSuperAdmin && waba.TenantId != tenantId)
            return ServiceResult<CredentialValidationResult>.Fail("WABA not found");

        var result = new CredentialValidationResult();

        // Validate AiSensy
        if (!string.IsNullOrEmpty(waba.AiSensyApiKey))
        {
            var decryptedKey = _encryption.Decrypt(waba.AiSensyApiKey);
            var aiSensy      = await _aiSensyValidator.ValidateApiKeyAsync(decryptedKey);
            result.AiSensyValid = aiSensy.IsValid;
            result.AiSensyError = aiSensy.Error;
        }

        // Validate Meta token
        if (!string.IsNullOrEmpty(waba.MetaAccessToken))
        {
            var decryptedToken   = _encryption.Decrypt(waba.MetaAccessToken);
            result.MetaTokenValid = await _metaGraph.ValidateTokenAsync(decryptedToken);
            if (!result.MetaTokenValid)
                result.MetaError = "Meta access token is invalid or expired";
        }

        result.QualityRating = waba.QualityRating;
        result.PhoneNumber   = waba.PhoneNumber;

        return ServiceResult<CredentialValidationResult>.Ok(result);
    }

    // ── Private helpers ───────────────────────────────────────

    private async Task<ServiceResult<WabaResponse>> CheckWabaLimitAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null)
            return ServiceResult<WabaResponse>.Fail("Tenant not found");

        var wabaCount = await _db.WabaAccounts
            .CountAsync(w => w.TenantId == tenantId);

        if (wabaCount >= tenant.MaxWabas)
            return ServiceResult<WabaResponse>.Fail(
                $"WABA limit reached ({tenant.MaxWabas}). " +
                "Please upgrade your plan to add more WhatsApp numbers.");

        return ServiceResult<WabaResponse>.Ok(null!); // Success means no limit issue
    }

    private static string GenerateWebhookVerifyToken()
    {
        // Unique token per WABA for Meta webhook verification
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")[..32];
    }

    private static WabaResponse ToResponse(WabaAccount w) => new()
    {
        Id                       = w.Id,
        TenantId                 = w.TenantId,
        DisplayName              = w.DisplayName,
        PhoneNumber              = w.PhoneNumber,
        PhoneNumberId            = w.PhoneNumberId ?? string.Empty,
        WabaId                   = w.WabaId ?? string.Empty,
        BusinessName             = w.BusinessName,
        BusinessDescription      = w.BusinessDescription,
        ProfilePictureUrl        = w.ProfilePictureUrl,
        Status                   = w.Status.ToString(),
        IsActive                 = w.IsActive,
        QualityRating            = w.QualityRating,
        MessagingTier            = w.MessagingTier,
        DailyMessageLimit        = w.DailyMessageLimit,
        MonthlyMessageLimit      = w.MonthlyMessageLimit,
        CurrentMonthMessageCount = w.CurrentMonthMessageCount,
        ConnectedAt              = w.ConnectedAt,
        CreatedAt                = w.CreatedAt,

        // Mask the API key — never expose raw credentials
        AiSensyKeyMasked = MaskKey(w.AiSensyApiKey),
        HasMetaToken     = !string.IsNullOrEmpty(w.MetaAccessToken),
    };

    private static string MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 8) return "****";
        return key[..8] + "****";
    }
}
