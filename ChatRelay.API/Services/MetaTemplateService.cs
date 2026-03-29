// ============================================================
//  ChatRelay — MetaTemplateService
//  All Meta Graph API template operations:
//  - Submit template for approval
//  - Fetch all templates from Meta
//  - Delete template from Meta
//  - Build correct Meta payload for every template type
// ============================================================

using ChatRelay.API.DTOs;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatRelay.API.Services;

public interface IMetaTemplateService
{
    Task<MetaTemplateSubmitResult> SubmitTemplateAsync(
        string wabaId, string accessToken,
        CreateTemplateRequest request);

    Task<List<MetaTemplateRecord>> FetchAllTemplatesAsync(
        string wabaId, string accessToken);

    Task<bool> DeleteTemplateAsync(
        string wabaId, string accessToken,
        string templateName, string metaTemplateId);
}

public class MetaTemplateService : IMetaTemplateService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<MetaTemplateService> _logger;

    private string ApiVersion => _config["Meta:ApiVersion"] ?? "v18.0";
    private string BaseUrl    => $"https://graph.facebook.com/{ApiVersion}";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MetaTemplateService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<MetaTemplateService> logger)
    {
        _httpClient = httpClient;
        _config     = config;
        _logger     = logger;
    }

    // ── Submit template to Meta ───────────────────────────────

    public async Task<MetaTemplateSubmitResult> SubmitTemplateAsync(
        string wabaId, string accessToken,
        CreateTemplateRequest request)
    {
        var url     = $"{BaseUrl}/{wabaId}/message_templates";
        var payload = BuildMetaPayload(request);
        var json    = JsonSerializer.Serialize(payload, JsonOpts);

        _logger.LogInformation(
            "Submitting template '{Name}' to Meta WABA {WabaId}",
            request.Name, wabaId);
        _logger.LogDebug("Meta template payload: {Json}", json);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient
            .SendAsync(httpRequest)
            .WaitAsync(TimeSpan.FromSeconds(30));

        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var result = JsonSerializer.Deserialize<MetaTemplateCreateResponse>(
                content, new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true });

            return new MetaTemplateSubmitResult
            {
                Success        = true,
                MetaTemplateId = result?.Id,
                Status         = result?.Status ?? "PENDING"
            };
        }

        var error = ParseMetaError(content);
        _logger.LogError(
            "Meta template submission failed for '{Name}': {Error}",
            request.Name, error);

        return new MetaTemplateSubmitResult
        {
            Success = false,
            Error   = error
        };
    }

    // ── Fetch all templates from Meta ─────────────────────────

    public async Task<List<MetaTemplateRecord>> FetchAllTemplatesAsync(
        string wabaId, string accessToken)
    {
        var templates = new List<MetaTemplateRecord>();
        var url = $"{BaseUrl}/{wabaId}/message_templates" +
                  $"?fields=id,name,status,category,language,components,rejected_reason" +
                  $"&limit=100" +
                  $"&access_token={accessToken}";

        // Handle pagination
        while (!string.IsNullOrEmpty(url))
        {
            var response = await _httpClient.GetAsync(url);
            var content  = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Failed to fetch templates from Meta: {Error}", content);
                break;
            }

            var page = JsonSerializer.Deserialize<MetaTemplateListResponse>(
                content, new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true });

            if (page?.Data != null)
                templates.AddRange(page.Data);

            // Get next page URL
            url = page?.Paging?.Next;
        }

        return templates;
    }

    // ── Delete template from Meta ─────────────────────────────

    public async Task<bool> DeleteTemplateAsync(
        string wabaId, string accessToken,
        string templateName, string metaTemplateId)
    {
        // Meta delete requires both name and HSMT ID
        var url = $"{BaseUrl}/{wabaId}/message_templates" +
                  $"?name={Uri.EscapeDataString(templateName)}" +
                  $"&hsm_id={metaTemplateId}";

        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        var content  = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Template '{Name}' deleted from Meta", templateName);
            return true;
        }

        _logger.LogError(
            "Failed to delete template '{Name}' from Meta: {Error}",
            templateName, content);
        return false;
    }

    // ── Build Meta API payload ────────────────────────────────

    private static object BuildMetaPayload(CreateTemplateRequest request)
    {
        // Carousel template
        if (request.CarouselCards?.Any() == true)
            return BuildCarouselPayload(request);

        // Standard template
        var components = new List<object>();

        // Header
        if (request.Header != null)
            components.Add(BuildHeaderComponent(request.Header));

        // Body (required)
        components.Add(BuildBodyComponent(request.Body));

        // Footer
        if (request.Footer != null)
            components.Add(new
            {
                type = "FOOTER",
                text = request.Footer.Text
            });

        // Buttons
        if (request.Buttons?.Any() == true)
            components.Add(BuildButtonsComponent(request.Buttons));

        return new
        {
            name       = request.Name.ToLower().Replace(" ", "_"),
            category   = request.Category.ToString().ToUpper(),
            language   = request.Language,
            components
        };
    }

    // ── Header ────────────────────────────────────────────────

    private static object BuildHeaderComponent(TemplateHeaderComponent header)
    {
        var format = header.Format.ToUpper();

        if (format == "TEXT")
        {
            var component = new Dictionary<string, object>
            {
                ["type"]   = "HEADER",
                ["format"] = "TEXT",
                ["text"]   = header.Text ?? string.Empty
            };

            if (header.TextExamples?.Any() == true)
                component["example"] = new
                {
                    header_text = header.TextExamples
                };

            return component;
        }

        // IMAGE | VIDEO | DOCUMENT | LOCATION
        var mediaComponent = new Dictionary<string, object>
        {
            ["type"]   = "HEADER",
            ["format"] = format
        };

        if (!string.IsNullOrEmpty(header.MediaExampleUrl) && format != "LOCATION")
        {
            mediaComponent["example"] = new
            {
                header_handle = new[] { header.MediaExampleUrl }
            };
        }

        return mediaComponent;
    }

    // ── Body ──────────────────────────────────────────────────

    private static object BuildBodyComponent(TemplateBodyComponent body)
    {
        var component = new Dictionary<string, object>
        {
            ["type"] = "BODY",
            ["text"] = body.Text
        };

        if (body.VariableExamples?.Any() == true)
        {
            component["example"] = new
            {
                body_text = new[] { body.VariableExamples }
            };
        }

        return component;
    }

    // ── Buttons ───────────────────────────────────────────────

    private static object BuildButtonsComponent(List<TemplateButtonComponent> buttons)
    {
        var builtButtons = buttons.Select(BuildSingleButton).ToList();

        return new
        {
            type    = "BUTTONS",
            buttons = builtButtons
        };
    }

    private static object BuildSingleButton(TemplateButtonComponent btn)
    {
        var type = btn.Type.ToUpper();

        return type switch
        {
            "QUICK_REPLY" => new
            {
                type = "QUICK_REPLY",
                text = btn.Text
            },

            "URL" => BuildUrlButton(btn),

            "PHONE_NUMBER" => new
            {
                type         = "PHONE_NUMBER",
                text         = btn.Text,
                phone_number = btn.PhoneNumber
            },

            "COPY_CODE" => new
            {
                type        = "COPY_CODE",
                example     = btn.CouponCode
            },

            "OTP" => BuildOtpButton(btn),

            _ => new { type, text = btn.Text }
        };
    }

    private static object BuildUrlButton(TemplateButtonComponent btn)
    {
        var urlType = (btn.UrlType ?? "STATIC").ToUpper();

        if (urlType == "DYNAMIC")
        {
            return new
            {
                type     = "URL",
                text     = btn.Text,
                url      = btn.Url,
                url_type = "DYNAMIC",
                example  = new[] { btn.UrlExample ?? "example" }
            };
        }

        return new
        {
            type     = "URL",
            text     = btn.Text,
            url      = btn.Url,
            url_type = "STATIC"
        };
    }

    private static object BuildOtpButton(TemplateButtonComponent btn)
    {
        var otpType = (btn.OtpType ?? "COPY_CODE").ToUpper();

        if (otpType == "ONE_TAP")
        {
            return new
            {
                type           = "OTP",
                otp_type       = "ONE_TAP",
                text           = btn.Text,
                package_name   = btn.PackageName,
                signature_hash = btn.SignatureHash
            };
        }

        return new
        {
            type     = "OTP",
            otp_type = otpType,
            text     = btn.Text
        };
    }

    // ── Carousel ──────────────────────────────────────────────

    private static object BuildCarouselPayload(CreateTemplateRequest request)
    {
        var cards = request.CarouselCards!.Select((card, index) =>
        {
            var cardComponents = new List<object>
            {
                // Carousel card header (IMAGE or VIDEO only)
                new
                {
                    type   = "HEADER",
                    format = card.Header.Format.ToUpper(),
                    example = string.IsNullOrEmpty(card.Header.MediaExampleUrl)
                        ? null
                        : new { header_handle = new[] { card.Header.MediaExampleUrl } }
                },
                // Card body
                BuildBodyComponent(card.Body)
            };

            // Card buttons (max 2)
            if (card.Buttons?.Any() == true)
                cardComponents.Add(BuildButtonsComponent(card.Buttons));

            return new { components = cardComponents };
        }).ToList();

        var components = new List<object>
        {
            // Carousel body (main message above cards)
            BuildBodyComponent(request.Body),
            new
            {
                type   = "CAROUSEL",
                cards
            }
        };

        if (request.Footer != null)
            components.Add(new { type = "FOOTER", text = request.Footer.Text });

        return new
        {
            name       = request.Name.ToLower().Replace(" ", "_"),
            category   = request.Category.ToString().ToUpper(),
            language   = request.Language,
            components
        };
    }

    // ── Error parser ──────────────────────────────────────────

    private static string ParseMetaError(string content)
    {
        try
        {
            var json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var msg)
                    ? msg.GetString() : null;
                var userMsg = error.TryGetProperty("error_user_msg", out var um)
                    ? um.GetString() : null;
                return userMsg ?? message ?? "Unknown Meta error";
            }
        }
        catch { /* fall through */ }
        return content.Length > 200 ? content[..200] : content;
    }
}

// ── Meta API response models ──────────────────────────────────

public class MetaTemplateSubmitResult
{
    public bool Success { get; set; }
    public string? MetaTemplateId { get; set; }
    public string? Status { get; set; }
    public string? Error { get; set; }
}

public class MetaTemplateCreateResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}

public class MetaTemplateListResponse
{
    [JsonPropertyName("data")]
    public List<MetaTemplateRecord>? Data { get; set; }

    [JsonPropertyName("paging")]
    public MetaPaging? Paging { get; set; }
}

public class MetaTemplateRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("components")]
    public List<JsonElement>? Components { get; set; }

    [JsonPropertyName("rejected_reason")]
    public string? RejectedReason { get; set; }
}

public class MetaPaging
{
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("previous")]
    public string? Previous { get; set; }
}
