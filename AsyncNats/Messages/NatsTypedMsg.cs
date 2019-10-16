namespace EightyDecibel.AsyncNats.Messages
{
    public class NatsTypedMsg<T> 
    {
        public string Subject { get; set; } = string.Empty;
        public string SubscriptionId { get; set; } = string.Empty;
        public string ReplyTo { get; set; } = string.Empty;
#nullable disable
        public T Payload { get; set; }
#nullable restore
    }
}