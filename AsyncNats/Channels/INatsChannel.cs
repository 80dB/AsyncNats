namespace EightyDecibel.AsyncNats.Channels
{
    using System;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Messages;

    internal interface INatsChannel : IAsyncDisposable
    {
        string? Subject { get; set; }
        string? QueueGroup { get; set; }
        string SubscriptionId { get; set; }

        ValueTask Publish(INatsServerMessage message);
    }
}