// ============================================================
//  ChatRelay — IMessageProvider + AiSensyProvider
//  All message types routed through AiSensy
// ============================================================

using ChatRelay.API.DTOs;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatRelay.API.Services;

// ── Provider abstraction ──────────────────────────────────────

public interface IMessageProvider
{
    Task<ProviderResult> SendAsync(ProviderSendRequest request);
}

public class ProviderSendRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string AiSensyApiKey { get; set; } = string.Empty;  // decrypted
    public MessagePayload Payload { get; set; } = null!;
    public string? WabaDisplayName { get; set; }
}

public class ProviderResult
{
    public bool Success { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
}

// ── AiSensy Provider ──────────────────────────────────────────

public class AiSensyProvider : IMessageProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiSensyProvider> _logger;

    private const string BaseUrl = "https://backend.aisensy.com/campaign/t1/api";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy    = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition  = JsonIgnoreCondition.WhenWritingNull
    };

    public AiSensyProvider(HttpClient httpClient, ILogger<AiSensyProvider> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
    }

    public async Task<ProviderResult> SendAsync(ProviderSendRequest request)
    {
        try
        {
            var body = BuildRequestBody(request);
            var json = JsonSerializer.Serialize(body, JsonOpts);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", request.AiSensyApiKey);

            var response = await _httpClient
                .SendAsync(httpRequest)
                .WaitAsync(TimeSpan.FromSeconds(30));

            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                string? messageId = null;
                try
                {
                    var parsed = JsonDocument.Parse(content);
                    if (parsed.RootElement.TryGetProperty("messageId", out var mid))
                        messageId = mid.GetString();
                    else if (parsed.RootElement.TryGetProperty("id", out var id))
                        messageId = id.GetString();
                }
                catch { /* messageId optional */ }

                return new ProviderResult { Success = true, ProviderMessageId = messageId };
            }

            // Parse AiSensy error
            var errorMessage = ParseAiSensyError(content, (int)response.StatusCode);
            _logger.LogWarning(
                "AiSensy send failed for {Phone}: {Error}", request.PhoneNumber, errorMessage);

            return new ProviderResult
            {
                Success   = false,
                Error     = errorMessage,
                ErrorCode = ((int)response.StatusCode).ToString()
            };
        }
        catch (TaskCanceledException)
        {
            return new ProviderResult
            {
                Success   = false,
                Error     = "Request to AiSensy timed out",
                ErrorCode = "TIMEOUT"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AiSensy send exception for {Phone}", request.PhoneNumber);
            return new ProviderResult
            {
                Success   = false,
                Error     = ex.Message,
                ErrorCode = "EXCEPTION"
            };
        }
    }

    // ── Build AiSensy request body per message type ───────────

    private static object BuildRequestBody(ProviderSendRequest request)
    {
        var payload = request.Payload;

        return payload.Type switch
        {
            MessagePayloadType.Text => BuildTextBody(request),
            MessagePayloadType.Image => BuildMediaBody(request, "image"),
            MessagePayloadType.Video => BuildMediaBody(request, "video"),
            MessagePayloadType.Audio => BuildMediaBody(request, "audio"),
            MessagePayloadType.Document => BuildMediaBody(request, "document"),
            MessagePayloadType.Sticker => BuildMediaBody(request, "sticker"),
            MessagePayloadType.Location => BuildLocationBody(request),
            MessagePayloadType.Contact => BuildContactBody(request),
            MessagePayloadType.Template => BuildTemplateBody(request),
            MessagePayloadType.Interactive => BuildInteractiveBody(request),
            _ => throw new NotSupportedException($"Message type {payload.Type} not supported")
        };
    }

    // ── Text ──────────────────────────────────────────────────

    private static object BuildTextBody(ProviderSendRequest r) => new
    {
        destination = r.PhoneNumber,
        userName    = r.WabaDisplayName ?? "ChatRelay",
        source      = "chatrelay",
        message     = new
        {
            type = "text",
            text = new
            {
                body       = r.Payload.Text!.Body,
                preview_url = r.Payload.Text.PreviewUrl
            }
        }
    };

    // ── Media (image, video, audio, document, sticker) ────────

    private static object BuildMediaBody(ProviderSendRequest r, string mediaType) => new
    {
        destination = r.PhoneNumber,
        userName    = r.WabaDisplayName ?? "ChatRelay",
        source      = "chatrelay",
        message = new
        {
            type    = mediaType,
            image   = mediaType == "image"    ? BuildMediaObject(r.Payload.Media!, mediaType) : null,
            video   = mediaType == "video"    ? BuildMediaObject(r.Payload.Media!, mediaType) : null,
            audio   = mediaType == "audio"    ? BuildMediaObject(r.Payload.Media!, mediaType) : null,
            document= mediaType == "document" ? BuildMediaObject(r.Payload.Media!, mediaType) : null,
            sticker = mediaType == "sticker"  ? BuildMediaObject(r.Payload.Media!, mediaType) : null,
        }
    };

    private static object BuildMediaObject(MediaPayload m, string type)
    {
        if (!string.IsNullOrEmpty(m.MetaMediaId))
            return new { id = m.MetaMediaId, caption = m.Caption, filename = m.FileName };

        return new { link = m.Url, caption = m.Caption, filename = m.FileName };
    }

    // ── Location ──────────────────────────────────────────────

    private static object BuildLocationBody(ProviderSendRequest r) => new
    {
        destination = r.PhoneNumber,
        userName    = r.WabaDisplayName ?? "ChatRelay",
        source      = "chatrelay",
        message = new
        {
            type     = "location",
            location = new
            {
                latitude  = r.Payload.Location!.Latitude,
                longitude = r.Payload.Location.Longitude,
                name      = r.Payload.Location.Name,
                address   = r.Payload.Location.Address
            }
        }
    };

    // ── Contact card ──────────────────────────────────────────

    private static object BuildContactBody(ProviderSendRequest r) => new
    {
        destination = r.PhoneNumber,
        userName    = r.WabaDisplayName ?? "ChatRelay",
        source      = "chatrelay",
        message = new
        {
            type     = "contacts",
            contacts = r.Payload.Contact!.Contacts.Select(c => new
            {
                name   = new { formatted_name = c.Name.FormattedName,
                               first_name     = c.Name.FirstName,
                               last_name      = c.Name.LastName },
                phones = c.Phones?.Select(p => new { phone = p.Phone, type = p.Type }),
                emails = c.Emails?.Select(e => new { email = e.Email, type = e.Type }),
                urls   = c.Urls?.Select(u => new { url = u.Url, type = u.Type }),
                org    = c.Org == null ? null : new { company = c.Org.Company, title = c.Org.Title }
            })
        }
    };

    // ── Template ──────────────────────────────────────────────

    private static object BuildTemplateBody(ProviderSendRequest r)
    {
        var t = r.Payload.Template!;
        var components = new List<object>();

        // Header component
        if (t.Header != null)
        {
            var headerParams = BuildHeaderParams(t.Header);
            if (headerParams != null)
                components.Add(new { type = "header", parameters = headerParams });
        }

        // Body component
        if (t.BodyParams?.Any() == true)
        {
            components.Add(new
            {
                type       = "body",
                parameters = t.BodyParams.Select(BuildTemplateParamObject)
            });
        }

        // Button components
        if (t.ButtonParams?.Any() == true)
        {
            foreach (var btn in t.ButtonParams)
            {
                components.Add(new
                {
                    type      = "button",
                    sub_type  = btn.Type.ToLower(),
                    index     = btn.Index.ToString(),
                    parameters = new[]
                    {
                        btn.Type.ToLower() == "url"
                            ? (object)new { type = "text", text = btn.Payload }
                            : new { type = "payload", payload = btn.Payload }
                    }
                });
            }
        }

        return new
        {
            destination = r.PhoneNumber,
            userName    = r.WabaDisplayName ?? "ChatRelay",
            source      = "chatrelay",
            message = new
            {
                type     = "template",
                template = new
                {
                    name     = t.Name,
                    language = new { code = t.Language },
                    components = components.Any() ? components : null
                }
            }
        };
    }

    private static List<object>? BuildHeaderParams(TemplateHeader header)
    {
        return header.Type.ToLower() switch
        {
            "text" => header.TextParams?
                .Select(BuildTemplateParamObject)
                .ToList<object>(),

            "image" => new List<object> { new
            {
                type  = "image",
                image = string.IsNullOrEmpty(header.MetaMediaId)
                    ? (object)new { link = header.MediaUrl }
                    : new { id   = header.MetaMediaId }
            }},

            "video" => new List<object> { new
            {
                type  = "video",
                video = string.IsNullOrEmpty(header.MetaMediaId)
                    ? (object)new { link = header.MediaUrl }
                    : new { id   = header.MetaMediaId }
            }},

            "document" => new List<object> { new
            {
                type     = "document",
                document = string.IsNullOrEmpty(header.MetaMediaId)
                    ? (object)new { link = header.MediaUrl }
                    : new { id   = header.MetaMediaId }
            }},

            "location" => new List<object> { new
            {
                type     = "location",
                location = new
                {
                    latitude  = header.Latitude,
                    longitude = header.Longitude,
                    name      = header.LocationName,
                    address   = header.LocationAddress
                }
            }},

            _ => null
        };
    }

    private static object BuildTemplateParamObject(TemplateParam p) =>
        p.Type.ToLower() switch
        {
            "currency"  => new { type = "currency", currency = new
            {
                fallback_value = p.Currency!.FallbackValue,
                code           = p.Currency.Code,
                amount_1000    = p.Currency.Amount1000
            }},
            "date_time" => new { type = "date_time", date_time = new
            {
                fallback_value = p.DateTime!.FallbackValue
            }},
            _ => new { type = "text", text = p.Text }
        };

    // ── Interactive ───────────────────────────────────────────

    private static object BuildInteractiveBody(ProviderSendRequest r)
    {
        var i = r.Payload.Interactive!;

        object? action = i.Type.ToLower() switch
        {
            "button" => new
            {
                buttons = i.Buttons?.Select(b => new
                {
                    type  = "reply",
                    reply = new { id = b.Id, title = b.Title }
                })
            },
            "list" => new
            {
                button   = i.List!.ButtonText,
                sections = i.List.Sections.Select(s => new
                {
                    title = s.Title,
                    rows  = s.Rows.Select(row => new
                    {
                        id          = row.Id,
                        title       = row.Title,
                        description = row.Description
                    })
                })
            },
            _ => null
        };

        return new
        {
            destination = r.PhoneNumber,
            userName    = r.WabaDisplayName ?? "ChatRelay",
            source      = "chatrelay",
            message = new
            {
                type        = "interactive",
                interactive = new
                {
                    type   = i.Type.ToLower(),
                    header = i.Header == null ? null : BuildInteractiveHeaderObject(i.Header),
                    body   = new { text = i.Body },
                    footer = i.Footer == null ? null : new { text = i.Footer },
                    action
                }
            }
        };
    }

    private static object BuildInteractiveHeaderObject(InteractiveHeader h) =>
        h.Type.ToLower() switch
        {
            "text"     => new { type = "text", text = h.Text },
            "image"    => new { type = "image", image =
                string.IsNullOrEmpty(h.MetaMediaId)
                    ? (object)new { link = h.MediaUrl }
                    : new { id = h.MetaMediaId } },
            "video"    => new { type = "video", video =
                string.IsNullOrEmpty(h.MetaMediaId)
                    ? (object)new { link = h.MediaUrl }
                    : new { id = h.MetaMediaId } },
            "document" => new { type = "document", document =
                string.IsNullOrEmpty(h.MetaMediaId)
                    ? (object)new { link = h.MediaUrl }
                    : new { id = h.MetaMediaId } },
            _ => new { type = h.Type }
        };

    // ── Error parser ──────────────────────────────────────────

    private static string ParseAiSensyError(string content, int statusCode)
    {
        try
        {
            var json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString() ?? $"AiSensy error ({statusCode})";
            if (json.RootElement.TryGetProperty("error", out var err))
                return err.GetString() ?? $"AiSensy error ({statusCode})";
        }
        catch { /* fall through */ }

        return statusCode switch
        {
            400 => "Invalid message request",
            401 => "Invalid AiSensy API key",
            403 => "AiSensy account suspended or limit reached",
            429 => "AiSensy rate limit exceeded",
            500 => "AiSensy service error",
            _ => $"AiSensy error ({statusCode})"
        };
    }
}
