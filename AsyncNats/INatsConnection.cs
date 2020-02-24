namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Channels;
    using EightyDecibel.AsyncNats.Messages;

    public interface INatsConnection : IAsyncDisposable
    {
        INatsOptions Options { get; }
        NatsStatus Status { get; }

        event EventHandler<Exception>? ConnectionException;
        event EventHandler<NatsStatus>? StatusChange;
        event EventHandler<NatsInformation>? ConnectionInformation;

        ValueTask ConnectAsync();
        ValueTask DisconnectAsync();

        ValueTask PublishObjectAsync<T>(string subject, T payload, string? replyTo = null, CancellationToken cancellationToken = default);
        ValueTask PublishAsync(string subject, byte[]? payload, string? replyTo = null, CancellationToken cancellationToken = default);
        ValueTask PublishTextAsync(string subject, string text, string? replyTo = null, CancellationToken cancellationToken = default);
        ValueTask PublishMemoryAsync(string subject, ReadOnlyMemory<byte> payload, string? replyTo = null, CancellationToken cancellationToken = default);

        IAsyncEnumerable<INatsServerMessage> SubscribeAll();
        ValueTask<INatsChannel> Subscribe(string subject, string? queueGroup = null, CancellationToken cancellationToken = default);
        ValueTask<INatsChannel<T>> Subscribe<T>(string subject, string? queueGroup = null, INatsSerializer? serializer = null, CancellationToken cancellationToken = default);
        ValueTask<INatsObjectChannel<T>> SubscribeObject<T>(string subject, string? queueGroup = null, INatsSerializer? serializer = null, CancellationToken cancellationToken = default);
        ValueTask<INatsChannel<string>> SubscribeText(string subject, string? queueGroup = null, CancellationToken cancellationToken = default);
        ValueTask Unsubscribe<T>(INatsObjectChannel<T> channel);
        ValueTask Unsubscribe<T>(INatsChannel<T> channel);
        ValueTask Unsubscribe(INatsChannel channel);

        Task<byte[]> Request(string subject, byte[] request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
        Task<ReadOnlyMemory<byte>> RequestMemory(string subject, Memory<byte> request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
        Task<string> RequestText(string subject, string request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
        Task<TResponse> RequestObject<TRequest, TResponse>(string subject, TRequest request, INatsSerializer? serializer = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

        TContract GenerateContractClient<TContract>(string? baseSubject = null);
        Task StartContractServer<TContract>(TContract contract, CancellationToken cancellationToken, string? baseSubject = null, string? queueGroup = null, INatsSerializer? serializer = null, TaskScheduler? taskScheduler = null);
    }
}