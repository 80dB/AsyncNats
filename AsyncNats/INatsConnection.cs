namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Messages;

    public interface INatsConnection : IAsyncDisposable
    {
        INatsOptions Options { get; }
        NatsStatus Status { get; }

        event EventHandler<Exception>? ConnectionException;
        event EventHandler<NatsStatus>? StatusChange;
        event EventHandler<NatsInformation>? ConnectionInformation;

        long SenderQueueSize { get; }
        long ReceiverQueueSize { get; }
        long TransmitBytesTotal { get; }
        long ReceivedBytesTotal { get; }
        long TransmitMessagesTotal { get; }
        long ReceivedMessagesTotal { get; }

        ValueTask ConnectAsync();
        ValueTask DisconnectAsync();

        ValueTask PublishObjectAsync<T>(NatsKey subject, T payload, NatsKey? replyTo = null, NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default);
        ValueTask PublishAsync(in NatsKey subject,in NatsPayload? payload = null,in NatsKey? replyTo = null,in NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default);

        IAsyncEnumerable<NatsMsg> Subscribe(NatsKey subject, NatsKey? queueGroup = null, CancellationToken cancellationToken = default);
        IAsyncEnumerable<NatsTypedMsg<T>> Subscribe<T>(NatsKey subject, NatsKey? queueGroup = null, INatsSerializer? serializer = null, CancellationToken cancellationToken = default);
        IAsyncEnumerable<T> SubscribeObject<T>(NatsKey subject, NatsKey? queueGroup = null, INatsSerializer? serializer = null, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> SubscribeText(NatsKey subject, NatsKey? queueGroup = null, CancellationToken cancellationToken = default);

        Task<byte[]> Request(NatsKey subject, NatsPayload request, TimeSpan? timeout = null, NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default);
        Task<TResponse> RequestObject<TRequest, TResponse>(NatsKey subject, TRequest request, INatsSerializer? serializer = null, TimeSpan? timeout = null, NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default);

        TContract GenerateContractClient<TContract>(string? baseSubject = null);
        Task StartContractServer<TContract>(TContract contract, CancellationToken cancellationToken, string? baseSubject = null, string? queueGroup = null, INatsSerializer? serializer = null, TaskScheduler? taskScheduler = null);
    }
}