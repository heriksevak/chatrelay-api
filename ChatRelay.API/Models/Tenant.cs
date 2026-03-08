namespace ChatRelay.API.Models
{
    public class Tenant
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Domain { get; set; }

        public string ApiKey { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}