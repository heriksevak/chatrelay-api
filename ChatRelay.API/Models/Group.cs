namespace ChatRelay.API.Models
{
    public class Group
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Name { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
