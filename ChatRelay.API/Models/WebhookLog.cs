namespace ChatRelay.API.Models
{
    public class WebhookLog
    {
        public int Id { get; set; }

        public string Payload { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
