using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class WhatsAppService
{
    private readonly HttpClient _httpClient;

    public WhatsAppService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> SendMessage(string phone, string message)
    {
        var url = "https://graph.facebook.com/v19.0/YOUR_PHONE_NUMBER_ID/messages";

        var payload = new
        {
            messaging_product = "whatsapp",
            to = phone,
            type = "text",
            text = new { body = message }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "YOUR_ACCESS_TOKEN");

        var response = await _httpClient.PostAsync(url, content);

        return await response.Content.ReadAsStringAsync();
    }
}