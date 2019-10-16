namespace EightyDecibel.AsyncNats.Channels
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Messages;

    internal interface INatsInternalChannel : IAsyncDisposable
    {
        string? Subject { get; }
        string? QueueGroup { get; }
        string SubscriptionId { get; }

        ValueTask Publish(INatsServerMessage message);
    }
}