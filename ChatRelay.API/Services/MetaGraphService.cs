// ============================================================
//  ChatRelay — MetaGraphService
//  Handles all Meta Graph API calls:
//  - Exchange Facebook SDK code → access token
//  - Fetch WABA profile + phone number details
//  - Validate token
//  - Media upload/download (later)
//  - Template management (later)
// ============================================================

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatRelay.API.Services;

public interface IMetaGraphService
{
    Task<MetaTokenResult> ExchangeCodeForTokenAsync(string code);
    Task<MetaWabaProfile> GetWabaProfileAsync(string wabaId, string accessToken);
    Task<MetaPhoneNumberProfile> GetPhoneNumberProfileAsync(
        string phoneNumberId, string accessToken);
    Task<bool> ValidateTokenAsync(string accessToken);
}

public class MetaGraphService : IMetaGraphService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<MetaGraphService> _logger;

    private string AppId     => _config["Meta:AppId"]!;
    private string AppSecret => _config["Meta:AppSecret"]!;
    private string ApiVersion => _config["Meta:ApiVersion"] ?? "v18.0";
    private string BaseUrl   => $"https://graph.facebook.com/{ApiVersion}";

    public MetaGraphService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<MetaGraphService> logger)
    {
        _httpClient = httpClient;
        _config     = config;
        _logger     = logger;
    }

    // ── Exchange code → access token ─────────────────────────
    // Facebook SDK returns a short-lived code.
    // We exchange it for a long-lived user access token,
    // then convert to a system user token for permanent use.

    public async Task<MetaTokenResult> ExchangeCodeForTokenAsync(string code)
    {
        var url = $"https://graph.facebook.com/{ApiVersion}/oauth/access_token" +
                  $"?client_id={AppId}" +
                  $"&client_secret={AppSecret}" +
                  $"&code={code}";

        var response = await _httpClient.GetAsync(url);
        var content  = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Meta token exchange failed: {Content}", content);
            throw new InvalidOperationException(
                $"Failed to exchange Facebook code for token: {content}");
        }

        var json = JsonSerializer.Deserialize<MetaTokenResponse>(content)
            ?? throw new InvalidOperationException("Empty response from Meta token endpoint");

        return new MetaTokenResult
        {
            AccessToken = json.AccessToken,
            TokenType   = json.TokenType,
            ExpiresIn   = json.ExpiresIn,
            ExpiresAt   = json.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(json.ExpiresIn.Value)
                : null
        };
    }

    // ── Get WABA profile ──────────────────────────────────────

    public async Task<MetaWabaProfile> GetWabaProfileAsync(
        string wabaId, string accessToken)
    {
        var url = $"{BaseUrl}/{wabaId}" +
                  $"?fields=name,currency,timezone_id,message_template_namespace" +
                  $"&access_token={accessToken}";

        var response = await _httpClient.GetAsync(url);
        var content  = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Meta WABA profile fetch failed: {Content}", content);
            throw new InvalidOperationException(
                $"Failed to fetch WABA profile from Meta: {content}");
        }

        return JsonSerializer.Deserialize<MetaWabaProfile>(content)
            ?? throw new InvalidOperationException("Empty WABA profile from Meta");
    }

    // ── Get phone number profile ──────────────────────────────

    public async Task<MetaPhoneNumberProfile> GetPhoneNumberProfileAsync(
        string phoneNumberId, string accessToken)
    {
        var url = $"{BaseUrl}/{phoneNumberId}" +
                  $"?fields=display_phone_number,verified_name,quality_rating," +
                  $"messaging_limit_tier,code_verification_status" +
                  $"&access_token={accessToken}";

        var response = await _httpClient.GetAsync(url);
        var content  = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Meta phone number profile fetch failed: {Content}", content);
            throw new InvalidOperationException(
                $"Failed to fetch phone number profile: {content}");
        }

        return JsonSerializer.Deserialize<MetaPhoneNumberProfile>(content)
            ?? throw new InvalidOperationException("Empty phone number profile from Meta");
    }

    // ── Validate token ────────────────────────────────────────

    public async Task<bool> ValidateTokenAsync(string accessToken)
    {
        try
        {
            var url = $"https://graph.facebook.com/debug_token" +
                      $"?input_token={accessToken}" +
                      $"&access_token={AppId}|{AppSecret}";

            var response = await _httpClient.GetAsync(url);
            var content  = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return false;

            var json = JsonDocument.Parse(content);
            return json.RootElement
                       .GetProperty("data")
                       .GetProperty("is_valid")
                       .GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Meta token validation error: {Message}", ex.Message);
            return false;
        }
    }
}

// ── Meta API response models ──────────────────────────────────

public class MetaTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }
}

public class MetaTokenResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = string.Empty;
    public int? ExpiresIn { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class MetaWabaProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("timezone_id")]
    public string? TimezoneId { get; set; }

    [JsonPropertyName("message_template_namespace")]
    public string? TemplateNamespace { get; set; }
}

public class MetaPhoneNumberProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("display_phone_number")]
    public string DisplayPhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("verified_name")]
    public string VerifiedName { get; set; } = string.Empty;

    [JsonPropertyName("quality_rating")]
    public string? QualityRating { get; set; }

    [JsonPropertyName("messaging_limit_tier")]
    public string? MessagingLimitTier { get; set; }

    [JsonPropertyName("code_verification_status")]
    public string? VerificationStatus { get; set; }
}
