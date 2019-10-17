namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Channels;
    using EightyDecibel.AsyncNats.Messages;

    public interface INatsConnection : IAsyncDisposable
    {
        INatsOptions Options { get; }

        ValueTask ConnectAsync();
        ValueTask DisconnectAsync();
        ValueTask PublishObjectAsync<T>(string subject, T payload, string? replyTo = null);
        ValueTask PublishAsync(string subject, byte[]? payload, string? replyTo = null);
        ValueTask PublishTextAsync(string subject, string text, string? replyTo = null);
        ValueTask PublishMemoryAsync(string subject, ReadOnlyMemory<byte> payload, string? replyTo = null);
        IAsyncEnumerable<INatsServerMessage> SubscribeAll();
        ValueTask<INatsChannel> Subscribe(string subject, string? queueGroup = null);
        ValueTask<INatsChannel<T>> Subscribe<T>(string subject, string? queueGroup = null, INatsSerializer? serializer = null);
        ValueTask<INatsChannel<string>> SubscribeText(string subject, string? queueGroup = null);
        ValueTask Unsubscribe<T>(INatsChannel<T> channel);
        ValueTask Unsubscribe(INatsChannel channel);
    }
}