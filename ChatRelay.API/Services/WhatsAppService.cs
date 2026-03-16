using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class WhatsAppService
{
    private readonly HttpClient _httpClient;

    private const string PhoneNumberId = "1037008952829198";
    private const string AccessToken = "EAANJJhQy5PMBQZBX7Gf0xSrkFpwZC60XwR9z3GU8mJGQP0JluZAmerqyBSqZBMvEnOe4H1AS0i4ZAdnxO9XVIZBZBMD82oZBTKE09MjM4nITJ1ZAo292rDT8ahJhSAyykIShEtEo8lZB5zRBkS3eR43EUDo0ta4d2AstN1KMKvc8wjsk8Rr397G95DDP3q9Y7HfQDB0Bs3usccS95ZCMoZCcYFiEAVwtnGEyWt0nQhc1DdG40K7OB7JJ9qRY9C5XjM6ksrlfFpZC2PZAcBtiIBSoXMit0dgtxG";

    public WhatsAppService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> SendMessage(string phone, string type, object payloadData)
        {
        var url = $"https://graph.facebook.com/v19.0/{PhoneNumberId}/messages";

        var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", AccessToken);

        request.Content = new StringContent(
            payloadData.ToString(),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.SendAsync(request);

        return await response.Content.ReadAsStringAsync();
    }
}