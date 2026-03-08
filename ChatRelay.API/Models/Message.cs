namespace ChatRelay.API.Models
{
    public class Message
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Phone { get; set; }

        public string Content { get; set; }

        public string Status { get; set; }

        public string ProviderMessageId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? SentAt { get; set; }

        public DateTime? DeliveredAt { get; set; }

        public DateTime? ReadAt { get; set; }
    }
}