namespace ChatRelay.API.Models
{
    public class Contact
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Name { get; set; }

        public string Phone { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}