namespace EightyDecibel.AsyncNats.Messages
{
    public class NatsTypedMsg<T>
    {
        public NatsKey Subject { get; set; } = NatsKey.Empty;
        public NatsKey SubscriptionId { get; set; } = NatsKey.Empty;
        public NatsKey ReplyTo { get; set; } = NatsKey.Empty;
#nullable disable
        public T Payload { get; set; }
#nullable restore
    }
}