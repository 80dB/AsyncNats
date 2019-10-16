namespace EightyDecibel.AsyncNats.Channels
{
    using System;
    using System.Collections.Generic;
    using EightyDecibel.AsyncNats.Messages;

    public interface INatsChannel : IAsyncEnumerable<INatsServerMessage>, IAsyncDisposable
    {
        string? Subject { get; }
        string? QueueGroup { get; }
        string SubscriptionId { get; }
    }

    public interface INatsChannel<T> : IAsyncEnumerable<NatsTypedMsg<T>>, IAsyncDisposable
    {
        string? Subject { get; }
        string? QueueGroup { get; }
        string SubscriptionId { get; }
    }
}