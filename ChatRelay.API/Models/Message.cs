using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatRelay.API.Models
{
    public class Message
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Phone { get; set; }

        public string Content { get; set; }
        public string Type { get; set; }

        public string Status { get; set; }

        public string? ProviderMessageId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? SentAt { get; set; }

        public DateTime? DeliveredAt { get; set; }

        public DateTime? ReadAt { get; set; }
    }
    public class SendMessageRequest
    {
        public string messaging_product { get; set; }
        public string recipient_type { get; set; }
        public string to { get; set; }
        public string type { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TextMessage? text { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ImageMessage? image { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DocumentMessage? document { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TemplateMessage? template { get; set; }
    }
    public class TextMessage
    {
        public bool preview_url { get; set; }
        public string body { get; set; }
    }
    public class ImageMessage
    {
        public string link { get; set; }
        public string caption { get; set; }
    }
    public class DocumentMessage
    {
        public string link { get; set; }
        public string caption { get; set; }
    }
    public class TemplateMessage
    {
        public string name { get; set; }
        public TemplateLanguage language { get; set; }
        public List<TemplateComponent> components { get; set; }
    }
    public class TemplateLanguage
    {
        public string code { get; set; }
    }
    public class TemplateComponent
    {
        public string type { get; set; }
        public List<TemplateParameter> parameters { get; set; }
    }

    public class TemplateParameter
    {
        public string type { get; set; }
        public string text { get; set; }
    }
}