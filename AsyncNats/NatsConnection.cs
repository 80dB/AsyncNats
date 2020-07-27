namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO.Pipelines;
    using System.Linq;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Messages;
    using EightyDecibel.AsyncNats.Rpc;

    public class NatsConnection : INatsConnection
    {
        private static long _nextSubscriptionId = 1;

        private long _senderQueueSize;
        private long _receiverQueueSize;

        private Task? _readWriteAsyncTask;
        private CancellationTokenSource? _disconnectSource;
        private Channel<IMemoryOwner<byte>> _senderChannel;

        private NatsMemoryPool _memoryPool;
        
        // Assignment of references are atomic
        // https://stackoverflow.com/questions/2192124/reference-assignment-is-atomic-so-why-is-interlocked-exchangeref-object-object
        private Subscription[] _subscriptions;
        private readonly SemaphoreSlim _subscriptionsLock; // This lock is to prevent double modification, not 'dirty read' by the process loop 

        private class Subscription
        {
            // Not sure if we need to keep a reference to Channel or not
            // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
            private readonly Channel<NatsMsg> _channel;

            public Subscription(string subject, string? queueGroup, long subscriptionId, int queueLength)
            {
                Subject = subject;
                QueueGroup = queueGroup;
                SubscriptionId = subscriptionId.ToString();

                _channel = Channel.CreateBounded<NatsMsg>(queueLength);

                Writer = _channel.Writer;
                Reader = _channel.Reader;
            }

            public string Subject { get; }
            public string? QueueGroup { get; }
            public string SubscriptionId { get; }
            
            public ChannelWriter<NatsMsg> Writer { get; }
            public ChannelReader<NatsMsg> Reader { get; }
        }

        private NatsStatus _status;
        private CancellationTokenSource _disposeTokenSource;

        public INatsOptions Options { get; }

        public NatsStatus Status
        {
            get => _status;
            private set
            {
                _status = value;
                StatusChange?.Invoke(this, value);
            }
        }

        public event EventHandler<Exception>? ConnectionException;
        public event EventHandler<NatsStatus>? StatusChange;
        public event EventHandler<NatsInformation>? ConnectionInformation;

        public long SenderQueueSize => _senderQueueSize;
        public long ReceiverQueueSize => _receiverQueueSize;

        public NatsConnection()
            : this(new NatsDefaultOptions())
        {
        }

        public NatsConnection(INatsOptions options)
        {
            Options = options;

            _memoryPool = new NatsMemoryPool(options.ArrayPool);

            _senderChannel = Channel.CreateBounded<IMemoryOwner<byte>>(options.SenderQueueLength);
            
            _subscriptions = Array.Empty<Subscription>();
            _subscriptionsLock = new SemaphoreSlim(1, 1);

            _disposeTokenSource = new CancellationTokenSource();
        }

        public ValueTask ConnectAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested) throw new ObjectDisposedException("Connection already disposed");
            if (_disconnectSource != null) throw new InvalidAsynchronousStateException("Already connected");

            _disconnectSource = new CancellationTokenSource();

            _senderQueueSize = 0;
            _readWriteAsyncTask = Task.Run(() => ReadWriteAsync(_disconnectSource.Token), _disconnectSource.Token);
            return new ValueTask();
        }

        private async Task ReadWriteAsync(CancellationToken disconnectToken)
        {
            while (!disconnectToken.IsCancellationRequested)
            {
                Status = NatsStatus.Connecting;

                using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.NoDelay = true;

                using var internalDisconnect = new CancellationTokenSource();

                try
                {
                    await socket.ConnectAsync(Options.Server);
                }
                catch (Exception ex)
                {
                    ConnectionException?.Invoke(this, ex);

                    await Task.Delay(TimeSpan.FromSeconds(1), disconnectToken);
                    continue;
                }

                _receiverQueueSize = 0;
                
                var readPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 1024*1024));
                // ReSharper disable AccessToDisposedClosure
                var readTask = Task.Run(() => ReadSocketAsync(socket, readPipe.Writer, internalDisconnect.Token), internalDisconnect.Token);
                var processTask = Task.Run(() => ProcessMessagesAsync(readPipe.Reader, internalDisconnect.Token), internalDisconnect.Token);
                var writeTask = Task.Run(() => WriteSocketAsync(socket, internalDisconnect.Token), internalDisconnect.Token);
                // ReSharper restore AccessToDisposedClosure
                try
                {
                    Status = NatsStatus.Connected;
                    Task.WaitAny(new[] {readTask, processTask, writeTask}, disconnectToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    ConnectionException?.Invoke(this, ex);
                }

                internalDisconnect.Cancel();
                await WaitAll(readTask, processTask, writeTask);
            }
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
                    ConnectionException?.Invoke(this, ex);
                }
            }
        }

        private async Task ReadSocketAsync(Socket socket, PipeWriter writer, CancellationToken disconnectToken)
        {
            while (!disconnectToken.IsCancellationRequested)
            {
                var memory = writer.GetMemory(socket.Available);

                var readBytes = await socket.ReceiveAsync(memory, SocketFlags.None, disconnectToken);
                if (readBytes == 0) break;

                writer.Advance(readBytes);
                Interlocked.Add(ref _receiverQueueSize, readBytes);

                var flush = await writer.FlushAsync(disconnectToken);
                if (flush.IsCompleted || flush.IsCanceled) break;
            }
        }

        private async Task ProcessMessagesAsync(PipeReader reader, CancellationToken disconnectToken)
        {
            var parser = new NatsMessageParser(_memoryPool);
            while (!disconnectToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(disconnectToken);
                if (read.IsCanceled) break;
                do
                {
                    var messages = parser.ParseMessages(read.Buffer, out var consumed);
                    reader.AdvanceTo(read.Buffer.GetPosition(consumed));
                    if (consumed == 0) break;

                    Interlocked.Add(ref _receiverQueueSize, (int)-consumed);

                    foreach (var message in messages)
                    {
                        switch (message)
                        {
                            case NatsPing _:
                                await WriteAsync(NatsPong.RentedSerialize(_memoryPool), disconnectToken);
                                break;

                            case NatsInformation info:
                                ConnectionInformation?.Invoke(this, info);
                                break;

                            case NatsMsg msg:
                                var subscriptions = _subscriptions;
                                foreach (var subscription in subscriptions)
                                {
                                    if (subscription.SubscriptionId != msg.SubscriptionId) continue;

                                    msg.Rent();
                                    if (subscription.Writer.TryWrite(msg)) continue;
                                    await subscription.Writer.WriteAsync(msg, disconnectToken);
                                }
                                msg.Release();
                                break;
                        }
                    }
                } while (reader.TryRead(out read));
            }
        }

        private async Task WriteSocketAsync(Socket socket, CancellationToken disconnectToken)
        {
            var reader = _senderChannel.Reader;
            var buffer = new byte[1024 * 1024];
            var bufferLength = buffer.Length;

            await SendConnect(socket, disconnectToken);
            await Resubscribe(socket, disconnectToken);
            while (!disconnectToken.IsCancellationRequested)
            {
                var position = 0;
                var result = await reader.ReadAsync(disconnectToken);
                do
                {
                    var consumed = result.Memory.Length;
                    Interlocked.Add(ref _senderQueueSize, -consumed);

                    if (position + consumed > bufferLength && position > 0)
                    {
                        await socket.SendAsync(buffer.AsMemory(0, position), SocketFlags.None, disconnectToken);
                        position = 0;
                    }

                    if (consumed > bufferLength)
                    {
                        await socket.SendAsync(result.Memory, SocketFlags.None, disconnectToken);
                    }
                    else
                    {
                        result.Memory.CopyTo(buffer.AsMemory(position));
                        position += consumed;
                    }
                } while (reader.TryRead(out result));

                if (position == 0) continue;

                await socket.SendAsync(buffer.AsMemory(0, position), SocketFlags.None, disconnectToken);
            }
        }

        private async Task SendConnect(Socket socket, CancellationToken disconnectToken)
        {
            var connect = new NatsConnect(Options);
            using var buffer = NatsConnect.RentedSerialize(_memoryPool, connect);
            await socket.SendAsync(buffer.Memory, SocketFlags.None, disconnectToken);
        }

        private async Task Resubscribe(Socket socket, CancellationToken disconnectToken)
        {
            await _subscriptionsLock.WaitAsync(disconnectToken);
            try
            {
                foreach (var subscription in _subscriptions)
                {
                    using var buffer = NatsSub.RentedSerialize(_memoryPool, subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId);
                    await socket.SendAsync(buffer.Memory, SocketFlags.None, disconnectToken);
                }
            }
            finally
            {
                _subscriptionsLock.Release();
            }
        }

        private ValueTask WriteAsync(IMemoryOwner<byte> buffer, CancellationToken cancellationToken)
        {
            Interlocked.Add(ref _senderQueueSize, buffer.Memory.Length);

            if (_senderChannel.Writer.TryWrite(buffer)) return new ValueTask();
            return _senderChannel.Writer.WriteAsync(buffer, cancellationToken);
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

            _subscriptions = Array.Empty<Subscription>();

            await DisconnectAsync();

            _disposeTokenSource.Cancel();
            _disposeTokenSource.Dispose();
        }

        public ValueTask PublishTextAsync(string subject, string text, string? replyTo = null, CancellationToken cancellationToken = default)
        {
            return PublishMemoryAsync(subject, Encoding.UTF8.GetBytes(text), replyTo, cancellationToken);
        }

        public ValueTask PublishObjectAsync<T>(string subject, T payload, string? replyTo = null, CancellationToken cancellationToken = default)
        {
            return PublishAsync(subject, Options.Serializer.Serialize(payload), replyTo, cancellationToken);
        }

        public ValueTask PublishAsync(string subject, byte[]? payload, string? replyTo = null, CancellationToken cancellationToken = default)
        {
            return PublishMemoryAsync(subject, payload?.AsMemory() ?? ReadOnlyMemory<byte>.Empty, replyTo, cancellationToken);
        }

        public async ValueTask PublishMemoryAsync(string subject, ReadOnlyMemory<byte> payload, string? replyTo = null, CancellationToken cancellationToken = default)
        {
            var pub = NatsPub.RentedSerialize(_memoryPool, subject, replyTo, payload);
            await WriteAsync(pub, cancellationToken);
        }

        private ValueTask SendSubscribe(Subscription subscription, CancellationToken cancellationToken = default)
        {
            return WriteAsync(NatsSub.RentedSerialize(_memoryPool, subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId), cancellationToken);
        }

        private ValueTask SendUnsubscribe(Subscription subscription)
        {
            return WriteAsync(NatsUnsub.RentedSerialize(_memoryPool, subscription.SubscriptionId, null), CancellationToken.None);
        }

        private async IAsyncEnumerable<T> InternalSubscribe<T>(string subject, string? queueGroup, Func<NatsMsg, T> deserialize, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var subscription = new Subscription(subject, queueGroup, Interlocked.Increment(ref _nextSubscriptionId), Options.ReceiverQueueLength);
            
            await _subscriptionsLock.WaitAsync(cancellationToken);
            try
            {
                _subscriptions = _subscriptions.Concat(new[] {subscription}).ToArray();
            }
            finally
            {
                _subscriptionsLock.Release();
            }

            await SendSubscribe(subscription, cancellationToken);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await subscription.Reader.ReadAsync(cancellationToken);
                    do
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

                        yield return msg;
                    } while (!cancellationToken.IsCancellationRequested && subscription.Reader.TryRead(out message));
                }
            }
            finally
            {
                // No cancellation token for the unsubscribe
                await _subscriptionsLock.WaitAsync(CancellationToken.None);
                try
                {
                    await SendUnsubscribe(subscription);
                    _subscriptions = _subscriptions.Where(s =>s != subscription).ToArray();
                }
                finally
                {
                    _subscriptionsLock.Release();
                }
            }
        }

        public async IAsyncEnumerable<NatsMsg> Subscribe(string subject, string? queueGroup = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            static NatsMsg Deserialize(NatsMsg msg)
            {
                return new NatsMsg
                {
                    Subject = msg.Subject,
                    ReplyTo = msg.ReplyTo,
                    Payload = msg.Payload.ToArray(),
                    SubscriptionId = msg.SubscriptionId
                };
            }

            await foreach (var msg in InternalSubscribe(subject, queueGroup, Deserialize, cancellationToken))
            {
                yield return msg;
            }
        }

        public async IAsyncEnumerable<string> SubscribeText(string subject, string? queueGroup = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            static string Deserialize(NatsMsg msg)
            {
                return Encoding.UTF8.GetString(msg.Payload.Span);
            }

            await foreach (var msg in InternalSubscribe(subject, queueGroup, Deserialize, cancellationToken))
            {
                yield return msg;
            }
        }

        public async IAsyncEnumerable<NatsTypedMsg<T>> Subscribe<T>(string subject, string? queueGroup = null, INatsSerializer? serializer = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NatsTypedMsg<T> Deserialize(NatsMsg msg)
            {
                return new NatsTypedMsg<T>
                {
                    Subject = msg.Subject,
                    ReplyTo = msg.ReplyTo,
                    SubscriptionId = msg.SubscriptionId,
                    Payload = (serializer ?? Options.Serializer).Deserialize<T>(msg.Payload)
                };
            }

            await foreach (var msg in InternalSubscribe(subject, queueGroup, Deserialize, cancellationToken))
            {

                yield return msg;
            }
        }

        public async IAsyncEnumerable<T> SubscribeObject<T>(string subject, string? queueGroup = null, INatsSerializer? serializer = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            T Deserialize(NatsMsg msg)
            {
                return (serializer ?? Options.Serializer).Deserialize<T>(msg.Payload);
            }

            await foreach (var msg in InternalSubscribe(subject, queueGroup, Deserialize, cancellationToken))
            {
                yield return msg;
            }
        }

        private async ValueTask<TResponse> InternalRequest<TResponse>(string subject, Memory<byte> request, Func<NatsMsg, TResponse> deserialize, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            // Combine cancellation token with timeout
            using var timeoutSource = new CancellationTokenSource(timeout ?? Options.RequestTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken);
            
            var replyTo = $"{Options.RequestPrefix}${Interlocked.Increment(ref _nextSubscriptionId)}";
            var subscription = new Subscription(replyTo, null, Interlocked.Increment(ref _nextSubscriptionId), Options.ReceiverQueueLength);

            await _subscriptionsLock.WaitAsync(cancellationToken);
            try
            {
                _subscriptions = _subscriptions.Concat(new[] {subscription}).ToArray();
            }
            finally
            {
                _subscriptionsLock.Release();
            }

            await SendSubscribe(subscription, cancellationToken);
            await PublishMemoryAsync(subject, request, replyTo, cancellationToken);
            try
            {
                var message = await subscription.Reader.ReadAsync(cancellationToken);
                try
                {
                    return deserialize(message);
                }
                catch (Exception e)
                {
                    ConnectionException?.Invoke(this, new NatsDeserializeException(message, e));
                    throw;
                }
            }
            finally
            {
                // No cancellation token for the unsubscribe
                await _subscriptionsLock.WaitAsync(CancellationToken.None);
                try
                {
                    await SendUnsubscribe(subscription);
                    _subscriptions = _subscriptions.Where(s =>s != subscription).ToArray();
                }
                finally
                {
                    _subscriptionsLock.Release();
                }
            }
        }

        public ValueTask<byte[]> Request(string subject, byte[] request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return InternalRequest(subject, request.AsMemory(), msg => msg.Payload.ToArray(), timeout, cancellationToken);
        }

        public ValueTask<Memory<byte>> RequestMemory(string subject, Memory<byte> request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return InternalRequest(subject, request, msg => msg.Payload.ToArray().AsMemory(), timeout, cancellationToken);
        }

        public ValueTask<string> RequestText(string subject, string request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return InternalRequest(subject, Encoding.UTF8.GetBytes(request), msg => Encoding.UTF8.GetString(msg.Payload.Span), timeout, cancellationToken);
        }

        public ValueTask<TResponse> RequestObject<TRequest, TResponse>(string subject, TRequest request, INatsSerializer? serializer = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return InternalRequest(subject, (serializer ?? Options.Serializer).Serialize(request), msg => (serializer ?? Options.Serializer).Deserialize<TResponse>(msg.Payload), timeout, cancellationToken);
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