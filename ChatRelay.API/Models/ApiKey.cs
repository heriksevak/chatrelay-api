namespace ChatRelay.API.Models
{
    public class ApiKey
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Key { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}