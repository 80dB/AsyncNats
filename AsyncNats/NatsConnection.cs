namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO.Pipelines;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Messages;
    using Rpc;
    using Microsoft.Extensions.Logging;
    using System.Collections.Concurrent;

    public class NatsConnection : INatsConnection
    {
        public NatsInformation? NatsInformation { get; private set; }

        private static long _nextSubscriptionId = 1;

        private readonly ILogger<NatsConnection>? _logger;

        private long _senderQueueSize;
        private long _receiverQueueSize;
        private long _transmitBytesTotal;
        private long _receivedBytesTotal;
        private long _transmitMessagesTotal;
        private long _receivedMessagesTotal;

        private Task? _readWriteAsyncTask;
        private CancellationTokenSource? _disconnectSource;
        private ReadOnlyMemory<byte> _reconnectResendBuffer = ReadOnlyMemory<byte>.Empty;

        private readonly NatsPublishChannel _senderChannel;
        private readonly NatsRequestResponse _requestResponse;
        private readonly NatsMemoryPool _memoryPool;
        private readonly ConcurrentDictionary<long, Subscription> _subscriptions = new ConcurrentDictionary<long, Subscription>();
        private readonly ConcurrentDictionary<long, InlineSubscription> _inlineSubscriptions = new ConcurrentDictionary<long, InlineSubscription>();


        private class Subscription
        {
            // Not sure if we need to keep a reference to Channel or not
            // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
            private readonly Channel<NatsMsg> _channel;

            public Subscription(NatsKey subject, NatsKey? queueGroup, long subscriptionId, int queueLength)
            {
                Subject = subject;
                QueueGroup = queueGroup ?? NatsKey.Empty;
                SubscriptionId = subscriptionId;

                _channel = Channel.CreateBounded<NatsMsg>(
                    new BoundedChannelOptions(queueLength)
                    {
                        SingleReader = true,
                        SingleWriter = true,
                        AllowSynchronousContinuations = true
                    });


                Writer = _channel.Writer;
                Reader = _channel.Reader;
            }

            public NatsKey Subject { get; }
            public NatsKey QueueGroup { get; }
            public long SubscriptionId { get; }
            
            public ChannelWriter<NatsMsg> Writer { get; }
            public ChannelReader<NatsMsg> Reader { get; }
        }

        internal class InlineSubscription
        {
            public NatsKey Subject { get; }
            public NatsKey QueueGroup { get; }
            public long SubscriptionId { get; }
            public NatsMessageInlineProcess Process { get; }

            public InlineSubscription(NatsKey subject, NatsKey? queueGroup, long subscriptionId, NatsMessageInlineProcess process)
            {
                Subject = subject;
                QueueGroup = queueGroup ?? NatsKey.Empty;
                SubscriptionId = subscriptionId;
                Process = process;
            }



        }

        private NatsStatus _status;
        private readonly CancellationTokenSource _disposeTokenSource;

        public INatsOptions Options { get; }

        public NatsStatus Status
        {
            get => _status;
            private set
            {
                _logger?.LogTrace("NatsConnection status changed from {Previous} to {Status}", _status, value);

                _status = value;
                StatusChange?.Invoke(this, value);
            }
        }

        public event EventHandler<Exception>? ConnectionException;
        public event EventHandler<NatsStatus>? StatusChange;
        public event EventHandler<NatsInformation>? ConnectionInformation;

        public long SenderQueueSize => _senderQueueSize;
        public long ReceiverQueueSize => _receiverQueueSize;
        public long TransmitBytesTotal => _transmitBytesTotal;
        public long ReceivedBytesTotal => _receivedBytesTotal;
        public long TransmitMessagesTotal => _transmitMessagesTotal;
        public long ReceivedMessagesTotal => _receivedMessagesTotal;

        public NatsConnection()
            : this(new NatsDefaultOptions())
        {
        }

        public NatsConnection(INatsOptions options)
        {
            Options = options;

            _memoryPool = new NatsMemoryPool(options.ArrayPool);

            _disposeTokenSource = new CancellationTokenSource();

            _senderChannel = new NatsPublishChannel(_memoryPool,_disposeTokenSource.Token);    

            _requestResponse = new NatsRequestResponse(this);

            _logger = options.LoggerFactory?.CreateLogger<NatsConnection>();
        }

        public ValueTask ConnectAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested)
            {
                _logger?.LogError("Connection already disposed");
                throw new ObjectDisposedException("Connection already disposed");
            }

            if (_disconnectSource != null)
            {
                _logger?.LogError("Already connected");
                throw new InvalidAsynchronousStateException("Already connected");
            }

            _disconnectSource = new CancellationTokenSource();

            _senderQueueSize = 0;
            _readWriteAsyncTask = Task.Run(() => ReadWriteAsync(_disconnectSource.Token), _disconnectSource.Token);
            return new ValueTask();
        }

        private async Task ReadWriteAsync(CancellationToken disconnectToken)
        {
            _logger?.LogTrace("Starting connection loop");
            while (!disconnectToken.IsCancellationRequested)
            {
                Status = NatsStatus.Connecting;

                using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.NoDelay = true;

                _senderChannel.DefaultBufferLength = socket.SendBufferSize;

                using var internalDisconnect = new CancellationTokenSource();

                try
                {
                    _logger?.LogTrace("Connecting to {Server}", Options.Server);
                    await socket.ConnectAsync(Options.Server);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error connecting to {Server}", Options.Server);
                    ConnectionException?.Invoke(this, ex);

                    await Task.Delay(TimeSpan.FromSeconds(1), disconnectToken);
                    continue;
                }

                _receiverQueueSize = 0;

                var readPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 1024 * 1024,useSynchronizationContext:false));
                
                // ReSharper disable AccessToDisposedClosure
                var readTask = Task.Run(() => ReadSocketAsync(socket, readPipe.Writer, internalDisconnect.Token), internalDisconnect.Token);
                var processTask = Task.Run(() => ProcessMessagesAsync(readPipe.Reader, internalDisconnect.Token), internalDisconnect.Token);
                var writeTask = Task.Run(() => WriteSocketAsync(socket, internalDisconnect.Token), internalDisconnect.Token);
                // ReSharper restore AccessToDisposedClosure
                try
                {
                    _logger?.LogTrace("Connected to {Server}", Options.Server);

                    Status = NatsStatus.Connected;
                    Task.WaitAny(new[] {readTask, processTask, writeTask}, disconnectToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Exception in the connection loop");

                    ConnectionException?.Invoke(this, ex);
                }

                internalDisconnect.Cancel();
                
                _logger?.LogTrace("Waiting for read/write/process tasks to finish");
                await WaitAll(readTask, processTask, writeTask);
            }
            _logger?.LogTrace("Exited connection loop");
        }

        private async Task WaitAll(params Task[] tasks)
        {
            foreach (var task in tasks)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Exception in a read/write/process task");

                    ConnectionException?.Invoke(this, ex);
                }
            }
        }

        private async Task ReadSocketAsync(Socket socket, PipeWriter writer, CancellationToken disconnectToken)
        {
            _logger?.LogTrace("Starting ReadSocketAsync loop");

            while (!disconnectToken.IsCancellationRequested)
            {
                var memory = writer.GetMemory(socket.ReceiveBufferSize);

                var readBytes = await socket.ReceiveAsync(memory, SocketFlags.None, disconnectToken).ConfigureAwait(false);
                if (readBytes == 0) break;

                writer.Advance(readBytes);
                Interlocked.Add(ref _receiverQueueSize, readBytes);
                Interlocked.Add(ref _receivedBytesTotal, readBytes);


                var flush = await writer.FlushAsync(disconnectToken).ConfigureAwait(false);
                if (flush.IsCompleted || flush.IsCanceled) break;
            }
            _logger?.LogTrace("Exited ReadSocketAsync loop");
        }
                
        private async Task ProcessMessagesAsync(PipeReader reader, CancellationToken disconnectToken)
        {
            _logger?.LogTrace("Starting ProcessMessagesAsync loop");
            var parser = new NatsMessageParser(_memoryPool,_inlineSubscriptions);

            INatsServerMessage[] parsedMessageBuffer = new INatsServerMessage[1024]; //arbitrary

            while (!disconnectToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(disconnectToken).ConfigureAwait(false);
               
                if (read.IsCanceled) break;
                do
                {                    
                    var parsedCount = parser.ParseMessages(read.Buffer, parsedMessageBuffer, out var consumed,out var inlined);                    
                    reader.AdvanceTo(read.Buffer.GetPosition(consumed));

                    Interlocked.Add(ref _receivedMessagesTotal, inlined);

                    if (consumed == 0) break;

                    Interlocked.Add(ref _receiverQueueSize, (long)-consumed);

                    for (var i = 0; i < parsedCount; i++)
                    {
                        var message = parsedMessageBuffer[i];

                        Interlocked.Increment(ref _receivedMessagesTotal);

                        switch (message)
                        {
                            case NatsMsg msg:
                                if (_subscriptions.TryGetValue(msg.SubscriptionId, out var subscription))
                                {
                                    msg.Rent();
                                    await subscription.Writer.WriteAsync(msg, disconnectToken).ConfigureAwait(false);
                                    msg.Release();
                                }
                                break;
                            case NatsPing _:
                                await WriteAsync(NatsPong.Instance, disconnectToken);
                                break;

                            case NatsInformation info:
                                _logger?.LogTrace("Received connection information for {Server}, {ConnectionInformation}", Options.Server, info);

                                NatsInformation = info;
                                ConnectionInformation?.Invoke(this, info);
                                break;                            
                        }
                    }
                } while (reader.TryRead(out read));
            }
            _logger?.LogTrace("Exited ReadSocketAsync loop");
        }

        private async Task WriteSocketAsync(Socket socket, CancellationToken disconnectToken)
        {
            _logger?.LogTrace("Starting WriteSocketAsync loop");

            ChannelReader<NatsPublishBuffer> reader = _senderChannel.Reader;

            await SendConnect(socket, disconnectToken);
            await Resubscribe(socket, disconnectToken);

            if (_reconnectResendBuffer.Length > 0)
            {
                _logger?.LogTrace("Sending saved reconnect resend buffer");
                await SocketSend(_reconnectResendBuffer);
                _reconnectResendBuffer = ReadOnlyMemory<byte>.Empty;
            }

            while (!disconnectToken.IsCancellationRequested)
            {          
                
                await foreach(var chunk in reader.ReadAllAsync(disconnectToken).ConfigureAwait(false))
                {
                    await chunk.Commit();

                    if (chunk.Messages > 0)
                    {
                        var memory = chunk.GetMemory();

                        Interlocked.Add(ref _senderQueueSize, -memory.Length);

                        await SocketSend(memory);

                        Interlocked.Add(ref _transmitMessagesTotal, chunk.Messages);
                        Interlocked.Add(ref _transmitBytesTotal, memory.Length);
                    }

                    _senderChannel.Return(chunk);
                }
            
            }

            _logger?.LogTrace("Exited WriteSocketAsync loop");

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            async ValueTask<int> SocketSend(ReadOnlyMemory<byte> data)
            {
                int sent = 0;
                try
                {
                    sent = await socket.SendAsync(data, SocketFlags.None, CancellationToken.None).ConfigureAwait(false);

                    //it seems there's no chance sent can be < data length for send success
                    return sent;
                }
                catch (Exception ex) {
                    _logger?.LogError(ex,"Error trying to write to socket");

                    if (sent < data.Length && data.Length<= socket.SendBufferSize)
                    {
                        _logger?.LogTrace("Saving reconnect resend buffer");

                        var buffer = new byte[data.Length - sent];
                        data.Slice(sent).CopyTo(buffer);
                        _reconnectResendBuffer = buffer;

                        throw new SocketException();
                    }
                    else
                        return sent;
                }
            }
        }

        private async Task SendConnect(Socket socket, CancellationToken disconnectToken)
        {
            var connect = new NatsConnect(Options);
            var buffer = NatsConnect.Serialize(connect);
            await socket.SendAsync(buffer, SocketFlags.None, disconnectToken);
        }

        private async Task Resubscribe(Socket socket, CancellationToken disconnectToken)
        {
            foreach (var (id, subscription) in _subscriptions)
            {
                _logger?.LogTrace("Resubscribing to {Subject} / {QueueGroup} / {SubscriptionId}", subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId);

                var sub = new NatsSub(subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId);
                var buffer =new byte[sub.Length];
                sub.Serialize(buffer);

                await socket.SendAsync(buffer, SocketFlags.None, disconnectToken);

                Interlocked.Add(ref _transmitBytesTotal, buffer.Length);
            }
        }

        private ValueTask WriteAsync<T>(in T message, CancellationToken cancellationToken) where T: INatsClientMessage
        {
            Interlocked.Add(ref _senderQueueSize, message.Length);

            return _senderChannel.Publish(message, cancellationToken);
        }

        public async ValueTask DisconnectAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested) throw new ObjectDisposedException("Connection already disposed");
            try
            {
                _disconnectSource?.Cancel();
                if (_readWriteAsyncTask != null)
                    await _readWriteAsyncTask;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _disconnectSource?.Dispose();
                _readWriteAsyncTask = null;
                _disconnectSource = null;

                Status = NatsStatus.Disconnected;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested) return;

            _subscriptions.Clear();

            await DisconnectAsync();

            _disposeTokenSource.Cancel();
            _disposeTokenSource.Dispose();
        }

        public ValueTask PublishObjectAsync<T>(NatsKey subject, T payload, NatsKey? replyTo = null, NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default)
        {
            return PublishAsync(subject, Options.Serializer.Serialize(payload), replyTo, headers, cancellationToken);
        }

        public ValueTask PublishAsync(in NatsKey subject, in NatsPayload? payload = null, in NatsKey? replyTo = null, in NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default)
        {
            if(headers == null)
            {
                return WriteAsync(new NatsPub(subject, replyTo ?? NatsKey.Empty, payload ?? NatsPayload.Empty),cancellationToken);
            }
            else
            {
                return WriteAsync(new NatsHPub(subject, replyTo ?? NatsKey.Empty, headers.Value, payload ?? NatsPayload.Empty), cancellationToken);
            }
        }

#if !DISABLE_PUBLISH_RAW
        public async ValueTask PublishRaw(byte[] rawData, int position, int messageCount, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            var natsBuffer = new NatsPublishBuffer(rawData, position, messageCount);
            natsBuffer.OnCommit += () => tcs.SetResult(true);
            await _senderChannel.Writer.WriteAsync(natsBuffer, cancellationToken);
            await tcs.Task;
            Interlocked.Add(ref _transmitMessagesTotal, messageCount);
        }
#endif

        private ValueTask SendSubscribe(Subscription subscription, CancellationToken cancellationToken = default)
        {
            _logger?.LogTrace("Subscribing to {Subject} / {QueueGroup} / {SubscriptionId}", subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId);

            return WriteAsync(new NatsSub(subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId), cancellationToken);
        }

        private ValueTask SendUnsubscribe(Subscription subscription)
        {
            _logger?.LogTrace("Unsubscribing from {Subject} / {QueueGroup} / {SubscriptionId}", subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId);

            return WriteAsync(new NatsUnsub( subscription.SubscriptionId, null), CancellationToken.None);
        }

        private async ValueTask InternalInlineSubscribe(NatsKey subject, NatsKey? queueGroup,NatsMessageInlineProcess process, CancellationToken cancellationToken = default)
        {
            var subscription = new InlineSubscription(subject, queueGroup, Interlocked.Increment(ref _nextSubscriptionId), process);

            _inlineSubscriptions.TryAdd(subscription.SubscriptionId, subscription);

            await WriteAsync(new NatsSub(subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId), cancellationToken);
            try
            {
                var tcs = new TaskCompletionSource<bool>();

                cancellationToken.Register(() => tcs.SetResult(true));

                await tcs.Task;
                
            }
            finally
            {
                await WriteAsync(new NatsUnsub(subscription.SubscriptionId, null), CancellationToken.None);
                _inlineSubscriptions.TryRemove(subscription.SubscriptionId,out _);
            }
        }

        private async IAsyncEnumerable<NatsMsg> InternalSubscribe(NatsKey subject, NatsKey? queueGroup, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var subscription = new Subscription(subject, queueGroup, Interlocked.Increment(ref _nextSubscriptionId), Options.ReceiverQueueLength);

            _subscriptions[subscription.SubscriptionId] = subscription;

            await SendSubscribe(subscription, cancellationToken);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await subscription.Reader.ReadAsync(cancellationToken);
                    do
                    {
                        yield return message;
                        
                    } while (!cancellationToken.IsCancellationRequested && subscription.Reader.TryRead(out message));
                }
            }
            finally
            {
                await SendUnsubscribe(subscription);
                _subscriptions.TryRemove(subscription.SubscriptionId, out _);
            }
        }
        private T DeserializeWrapper<T>(Func<NatsMsg, T> deserialize, NatsMsg message)
        {
            T msg;
            try
            {
                msg = deserialize(message);
            }
            catch (Exception e)
            {
                ConnectionException?.Invoke(this, new NatsDeserializeException(message, e));
                throw;
            }
            finally
            {
                message.Release();
            }

            return msg;
        }

        public async IAsyncEnumerable<NatsMsg> Subscribe(NatsKey subject, NatsKey? queueGroup = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            static NatsMsg Deserialize(NatsMsg msg)
            {
                return new NatsMsg(msg.Subject, msg.SubscriptionId, msg.ReplyTo, msg.Payload.ToArray());
            }

            await foreach (var msg in InternalSubscribe(subject, queueGroup, cancellationToken))
            {
                yield return DeserializeWrapper(Deserialize, msg);
            }
        }

        /// <summary>
        /// Warning, NatsMsg object will by recycled after each iteration. 
        /// </summary>
        /// <param name="subject"></param>
        /// <param name="queueGroup"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async IAsyncEnumerable<NatsMsg> SubscribeUnsafe(NatsKey subject, NatsKey? queueGroup = null, [EnumeratorCancellation]  CancellationToken cancellationToken = default)
        {
            await foreach (var msg in InternalSubscribe(subject, queueGroup, cancellationToken))
            {
                try
                {
                    yield return msg;
                }
                finally
                {
                    msg.Release();
                }
            }
        }
                
        public async ValueTask SubscribeUnsafe(NatsKey subject, NatsMessageInlineProcess process, CancellationToken cancellationToken = default)
        {          
            await InternalInlineSubscribe(subject, NatsKey.Empty, process, cancellationToken);
        }
        
        public async ValueTask SubscribeUnsafe(NatsKey subject, NatsKey queueGroup,NatsMessageInlineProcess process, CancellationToken cancellationToken = default)
        {
            await InternalInlineSubscribe(subject, queueGroup, process, cancellationToken);
        }

        public async IAsyncEnumerable<string> SubscribeText(NatsKey subject, NatsKey? queueGroup = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            static string Deserialize(NatsMsg msg)
            {
                return Encoding.UTF8.GetString(msg.Payload.Span);
            }

            await foreach (var msg in InternalSubscribe(subject, queueGroup, cancellationToken))
            {
                yield return DeserializeWrapper(Deserialize, msg);
            }
        }
                       
        public async IAsyncEnumerable<NatsTypedMsg<T>> Subscribe<T>(NatsKey subject, NatsKey? queueGroup = null, INatsSerializer? serializer = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NatsTypedMsg<T> Deserialize(NatsMsg msg)
            {                
                return new NatsTypedMsg<T>
                {
                    Subject = msg.Subject,
                    ReplyTo = msg.ReplyTo,
                    SubscriptionId = msg.SubscriptionId.ToString(),
                    Payload = (serializer ?? Options.Serializer).Deserialize<T>(msg.Payload)
                };
            }

            await foreach (var msg in InternalSubscribe(subject, queueGroup, cancellationToken))
            {
                yield return DeserializeWrapper(Deserialize, msg);
            }
        }

        public async IAsyncEnumerable<T> SubscribeObject<T>(NatsKey subject, NatsKey? queueGroup = null, INatsSerializer? serializer = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            T Deserialize(NatsMsg msg)
            {
                return (serializer ?? Options.Serializer).Deserialize<T>(msg.Payload);
            }

            await foreach (var msg in InternalSubscribe(subject, queueGroup, cancellationToken))
            {
                yield return DeserializeWrapper(Deserialize, msg);
            }
        }

        public Task<byte[]> Request(NatsKey subject, NatsPayload request, TimeSpan? timeout = null, NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default)
        {
            return _requestResponse.InternalRequest(subject, request, msg => msg.Payload.ToArray(), timeout, headers, cancellationToken);
        }

        public Task<TResponse> RequestObject<TRequest, TResponse>(NatsKey subject, TRequest request, INatsSerializer? serializer = null, TimeSpan? timeout = null, NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default)
        {
            return _requestResponse.InternalRequest(subject, (serializer ?? Options.Serializer).Serialize(request), msg => (serializer ?? Options.Serializer).Deserialize<TResponse>(msg.Payload), timeout, headers, cancellationToken);
        }

        public TContract GenerateContractClient<TContract>(string? baseSubject = null)
        {
            baseSubject ??= $"{typeof(TContract).Namespace}.{typeof(TContract).Name}";
            return NatsClientGenerator<TContract>.GenerateClient(this, baseSubject);
        }

        public async Task StartContractServer<TContract>(TContract contract, CancellationToken cancellationToken, string? baseSubject = null, string? queueGroup = null, INatsSerializer? serializer = null, TaskScheduler? taskScheduler = null)
        {
            baseSubject ??= $"{typeof(TContract).Namespace}.{typeof(TContract).Name}";
            baseSubject += ".>";

            await NatsServerGenerator<TContract>.CreateServerProxy(this, baseSubject, queueGroup,  serializer ?? Options.Serializer, contract, taskScheduler).Listener(cancellationToken);
        }

        internal void ServerException(object sender, NatsMsg msg, Exception exception)
        {
            ConnectionException?.Invoke(sender, new NatsServerException(msg, exception));
        }
    }

}