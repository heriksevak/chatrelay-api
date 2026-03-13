using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class WhatsAppService
{
    private readonly HttpClient _httpClient;

    private const string PhoneNumberId = "1037008952829198";
    private const string AccessToken = "EAANJJhQy5PMBQ5qeVzX85GlZBVtz0JRcjmbZBDqkQXinb5Kjs642KR9uwmyP9wXQg7hlw7d9x5Cf73JIkRxAPn8mm5lfiTUZA2Yk5Y0SidwNAzphxGIxTYBlJPXwa0F9SVBuWaGDIhZBwVw8005061gIDZB1hT9W6C9In4JJZBs6zxNZCi0Ag40QiZBZB83WwnSwV5rgNY4eQspwbltpqBxG4ThJS2x0YwuRAlfg87FzOtN9JEJuBwnbBaHWI9bRBnwLclkmOjZApaU3w90ZAJV4GaFZBYmz";

    public WhatsAppService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> SendMessage(string phone, string message)
    {
        var url = $"https://graph.facebook.com/v19.0/{PhoneNumberId}/messages";

        var payload = new
        {
            messaging_product = "whatsapp",
            to = phone,
            type = "text",
            text = new { body = message }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

        request.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(request);

        var result = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(result);

        return doc.RootElement
            .GetProperty("messages")[0]
            .GetProperty("id")
            .GetString();
    }
}