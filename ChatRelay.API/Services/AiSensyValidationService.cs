// ============================================================
//  ChatRelay — AiSensyValidationService
//  Validates AiSensy API key by making a lightweight test call
// ============================================================

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatRelay.API.Services;

public interface IAiSensyValidationService
{
    Task<AiSensyValidationResult> ValidateApiKeyAsync(string apiKey);
}

public class AiSensyValidationService : IAiSensyValidationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiSensyValidationService> _logger;

    // AiSensy endpoint to fetch account details — lightweight validation call
    private const string ValidationUrl =
        "https://backend.aisensy.com/campaign/t1/api/account";

    public AiSensyValidationService(
        HttpClient httpClient,
        ILogger<AiSensyValidationService> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
    }

    public async Task<AiSensyValidationResult> ValidateApiKeyAsync(string apiKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ValidationUrl);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient
                .SendAsync(request)
                .WaitAsync(TimeSpan.FromSeconds(10)); // 10 second timeout

            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // Try parse account name from response
                string? accountName = null;
                try
                {
                    var json = JsonDocument.Parse(content);
                    if (json.RootElement.TryGetProperty("name", out var name))
                        accountName = name.GetString();
                }
                catch { /* ignore parse errors */ }

                return new AiSensyValidationResult
                {
                    IsValid     = true,
                    AccountName = accountName
                };
            }

            // 401 = invalid key, 403 = key suspended
            var error = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Invalid AiSensy API key",
                System.Net.HttpStatusCode.Forbidden    => "AiSensy account suspended",
                _ => $"AiSensy validation failed ({(int)response.StatusCode})"
            };

            return new AiSensyValidationResult { IsValid = false, Error = error };
        }
        catch (TaskCanceledException)
        {
            return new AiSensyValidationResult
            {
                IsValid = false,
                Error   = "AiSensy validation timed out. Please try again."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AiSensy validation error");
            return new AiSensyValidationResult
            {
                IsValid = false,
                Error   = "Could not reach AiSensy. Check your internet connection."
            };
        }
    }
}

public class AiSensyValidationResult
{
    public bool IsValid { get; set; }
    public string? AccountName { get; set; }
    public string? Error { get; set; }
}
