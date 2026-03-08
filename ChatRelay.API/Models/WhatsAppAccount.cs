namespace ChatRelay.API.Models
{
    public class WhatsAppAccount
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string PhoneNumberId { get; set; }

        public string AccessToken { get; set; }

        public string BusinessAccountId { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}