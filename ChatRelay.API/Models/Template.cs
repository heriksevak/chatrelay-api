namespace ChatRelay.API.Models
{
    public class Template
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Name { get; set; }

        public string Content { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}