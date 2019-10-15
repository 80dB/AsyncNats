namespace EightyDecibel.AsyncNats.Messages
{
    public class NatsTypedMsg<T> where T : class
    {
        public string Subject { get; set; } = string.Empty;
        public string SubscriptionId { get; set; } = string.Empty;
        public string ReplyTo { get; set; } = string.Empty;
        public T? Payload { get; set; } = default(T);
    }
}