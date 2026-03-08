namespace ChatRelay.API.Models
{
    public class IncomingMessage
    {
        public int Id { get; set; }

        public string SenderPhone { get; set; }

        public string MessageText { get; set; }

        public DateTime ReceivedAt { get; set; }
    }
}