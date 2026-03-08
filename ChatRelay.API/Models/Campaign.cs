namespace ChatRelay.API.Models
{
    public class Campaign
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Name { get; set; }

        public int TemplateId { get; set; }

        public string Status { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}