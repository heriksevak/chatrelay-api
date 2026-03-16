using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class WhatsAppService
{
    private readonly HttpClient _httpClient;

    private const string PhoneNumberId = "1037008952829198";
    private const string AccessToken = "EAANJJhQy5PMBQzyqygoy1pZCZCN8ubZAepmSkLFVmmtCm3GkAZAOSjbuvp4Ts0is3AaEiM5zJimGXm5FCb6hQWi6jBcLCIjZBNs5Aiq0HEGX1hC0cAUs0y7uZBNQ8g0B5UuIQ19r9ZB0xgtunMfIxRj9v9P4KMlX6pMtmILBgB8R0gmCVQk1DSf6wyDLeu68lmrjZAYd8YZAceCFJybnGpVqz1m526i9SAvtDDP5KWxEDjpRQl8apRtzlwZAdJhCB64zRuHLmLwiJZCY5s6nCr1KMOaIxgSUccZD";

    public WhatsAppService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> SendMessage(string phone, string type, object payloadData)
    {
        var url = $"https://graph.facebook.com/v19.0/{PhoneNumberId}/messages";

        var payload = new Dictionary<string, object>
        {
            { "messaging_product", "whatsapp" },
            { "to", phone },
            { "type", type },
            { type, payloadData }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", AccessToken);

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.SendAsync(request);

        return await response.Content.ReadAsStringAsync();
    }
}