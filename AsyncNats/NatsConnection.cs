namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO.Pipelines;
    using System.Linq;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Channels;
    using EightyDecibel.AsyncNats.Messages;
    using EightyDecibel.AsyncNats.Rpc;

    public class NatsConnection : INatsConnection
    {
        private static long _nextSubscriptionId = 1;

        private long _senderQueueSize;
        private long _receiverQueueSize;

        private Task? _readWriteAsyncTask;
        private CancellationTokenSource? _disconnectSource;
        private Channel<byte[]> _senderChannel;
        private ConcurrentDictionary<string, INatsInternalChannel> _channels;

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

            _senderChannel = Channel.CreateBounded<byte[]>(options.SenderQueueLength);
            _channels = new ConcurrentDictionary<string, INatsInternalChannel>();
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

                var readPipe = new Pipe(Options.ReceiverPipeOptions);
                var readTask = Task.Run(() => ReadSocketAsync(socket, readPipe.Writer, internalDisconnect.Token), internalDisconnect.Token);
                var processTask = Task.Run(() => ProcessMessagesAsync(readPipe.Reader, internalDisconnect.Token), internalDisconnect.Token);

                var writeTask = Task.Run(() => WriteSocketAsync(socket, internalDisconnect.Token), internalDisconnect.Token);
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
                var memory = writer.GetMemory();

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
            var parser = new NatsMessageParser();
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
                        if (message is NatsPing)
                        {
                            await WriteAsync(NatsPong.RentedSerialize(), disconnectToken);
                        }

                        if (message is NatsInformation info)
                        {
                            ConnectionInformation?.Invoke(this, info);
                        }

                        var msg = message as NatsMsg;
                        var subscriptionId = msg?.SubscriptionId;
                        foreach (var channel in _channels.Values)
                        {
                            if (channel.SubscriptionId != null && subscriptionId != channel.SubscriptionId) continue;
                            await channel.Publish(message, disconnectToken);
                        }

                        msg?.Release();
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
                    var consumed = BitConverter.ToInt32(result);
                    Interlocked.Add(ref _senderQueueSize, -consumed);

                    if (position + consumed > bufferLength && position > 0)
                    {
                        await socket.SendAsync(buffer.AsMemory(0, position), SocketFlags.None, disconnectToken);
                        position = 0;
                    }

                    if (consumed > bufferLength)
                    {
                        await socket.SendAsync(result.AsMemory(4, consumed), SocketFlags.None, disconnectToken);
                    }
                    else
                    {
                        result.AsMemory(4, consumed).CopyTo(buffer.AsMemory(position));
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
            var buffer = NatsConnect.RentedSerialize(connect);
            var consumed = BitConverter.ToInt32(buffer);
            await socket.SendAsync(buffer.AsMemory(4, consumed), SocketFlags.None, disconnectToken);
            ArrayPool<byte>.Shared.Return(buffer);
        }

        private async Task Resubscribe(Socket socket, CancellationToken disconnectToken)
        {
            foreach (var channel in _channels.Values)
            {
                if (string.IsNullOrEmpty(channel.Subject))
                    continue;

                var buffer = NatsSub.RentedSerialize(channel.Subject, channel.QueueGroup, channel.SubscriptionId);
                var consumed = BitConverter.ToInt32(buffer);
                await socket.SendAsync(buffer.AsMemory(4, consumed), SocketFlags.None, disconnectToken);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private ValueTask WriteAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            var consumed = BitConverter.ToInt32(buffer);
            Interlocked.Add(ref _senderQueueSize, consumed);

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

            foreach (var channel in _channels.Values.ToArray())
                await channel.DisposeAsync();

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
            var pub = NatsPub.RentedSerialize(subject, replyTo, payload);
            await WriteAsync(pub, cancellationToken);
        }

        public IAsyncEnumerable<INatsServerMessage> SubscribeAll()
        {
            var subscriptionId = Interlocked.Increment(ref _nextSubscriptionId).ToString();
            var channel = new NatsUntypedChannel(this, null, null, subscriptionId);
            _channels[subscriptionId] = channel;
            return channel;
        }

        private async ValueTask<string> SendSubscribe(string subject, string? queueGroup, CancellationToken cancellationToken = default)
        {
            var subscriptionId = Interlocked.Increment(ref _nextSubscriptionId).ToString();
            await WriteAsync(NatsSub.RentedSerialize(subject, queueGroup, subscriptionId), cancellationToken);
            return subscriptionId;
        }

        public async ValueTask<INatsChannel> Subscribe(string subject, string? queueGroup = null, CancellationToken cancellationToken = default)
        {
            var subscriptionId = await SendSubscribe(subject, queueGroup, cancellationToken);
            var channel = new NatsUntypedChannel(this, subject, queueGroup, subscriptionId);
            _channels[subscriptionId] = channel;
            return channel;
        }

        public async ValueTask<INatsChannel<string>> SubscribeText(string subject, string? queueGroup = null, CancellationToken cancellationToken = default)
        {
            var subscriptionId = await SendSubscribe(subject, queueGroup, cancellationToken);
            var channel = new NatsTextChannel(this, subject, queueGroup, subscriptionId);
            _channels[subscriptionId] = channel;
            return channel;
        }

        public async ValueTask<INatsChannel<T>> Subscribe<T>(string subject, string? queueGroup = null, INatsSerializer? serializer = null, CancellationToken cancellationToken = default)
        {
            var subscriptionId = await SendSubscribe(subject, queueGroup, cancellationToken);
            var channel = new NatsTypedChannel<T>(this, subject, queueGroup, subscriptionId, serializer ?? Options.Serializer);
            _channels[subscriptionId] = channel;
            return channel;
        }

        public async ValueTask<INatsObjectChannel<T>> SubscribeObject<T>(string subject, string? queueGroup = null, INatsSerializer? serializer = null, CancellationToken cancellationToken = default)
        {
            var subscriptionId = await SendSubscribe(subject, queueGroup, cancellationToken);
            var channel = new NatsObjectChannel<T>(this, subject, queueGroup, subscriptionId, serializer ?? Options.Serializer);
            _channels[subscriptionId] = channel;
            return channel;
        }

        public ValueTask Unsubscribe<T>(INatsObjectChannel<T> channel) => Unsubscribe(channel as INatsInternalChannel);

        public ValueTask Unsubscribe<T>(INatsChannel<T> channel) => Unsubscribe(channel as INatsInternalChannel);

        public ValueTask Unsubscribe(INatsChannel channel) => Unsubscribe(channel as INatsInternalChannel);

        internal async ValueTask Unsubscribe(INatsInternalChannel? channel)
        {
            if (channel == null) return;
            if (!_channels.TryRemove(channel.SubscriptionId, out var dummy)) return;
            if (string.IsNullOrEmpty(channel.Subject)) return;

            await WriteAsync(NatsUnsub.RentedSerialize(channel.SubscriptionId, null), CancellationToken.None);
        }

        public async Task<byte[]> Request(string subject, byte[] request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return (await RequestMemory(subject, request, timeout, cancellationToken)).ToArray();
        }

        public async Task<ReadOnlyMemory<byte>> RequestMemory(string subject, Memory<byte> request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var replyTo = $"{Options.RequestPrefix}${Interlocked.Increment(ref _nextSubscriptionId)}";
            await using var replySubscription = await Subscribe(replyTo, cancellationToken: cancellationToken);
            await PublishMemoryAsync(subject, request, replyTo, cancellationToken);

            using var timeoutSource = new CancellationTokenSource(timeout ?? Options.RequestTimeout);
            var linkedSource = cancellationToken != default ? CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken) : null;
            try
            {
                await foreach (var response in replySubscription.WithCancellation((linkedSource ?? timeoutSource).Token))
                {
                    if (!(response is NatsMsg msg)) continue;
                    return msg.Payload;
                }
            }
            finally
            {
                linkedSource?.Dispose();
            }

            throw new TimeoutException();
        }

        public async Task<string> RequestText(string subject, string request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var replyTo = $"{Options.RequestPrefix}${Interlocked.Increment(ref _nextSubscriptionId)}";
            await using var replySubscription = await SubscribeText(replyTo, cancellationToken: cancellationToken);
            await PublishTextAsync(subject, request, replyTo, cancellationToken);

            using var timeoutSource = new CancellationTokenSource(timeout ?? Options.RequestTimeout);
            var linkedSource = cancellationToken != default ? CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken) : null;
            try
            {
                await foreach (var response in replySubscription.WithCancellation((linkedSource ?? timeoutSource).Token))
                    return response.Payload;
            }
            finally
            {
                linkedSource?.Dispose();
            }

            throw new TimeoutException();
        }

        public async Task<TResponse> RequestObject<TRequest, TResponse>(string subject, TRequest request, INatsSerializer? serializer = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var replyTo = $"{Options.RequestPrefix}${Interlocked.Increment(ref _nextSubscriptionId)}";
            await using var replySubscription = await SubscribeObject<TResponse>(replyTo, serializer: serializer, cancellationToken: cancellationToken);
            await PublishObjectAsync(subject, request, replyTo, cancellationToken);

            using var timeoutSource = new CancellationTokenSource(timeout ?? Options.RequestTimeout);
            var linkedSource = cancellationToken != default ? CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken) : null;
            try
            {
                await foreach (var response in replySubscription.WithCancellation((linkedSource ?? timeoutSource).Token))
                    return response;
            }
            finally
            {
                linkedSource?.Dispose();
            }

            throw new TimeoutException();
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

            var subscriptionId = await SendSubscribe(baseSubject, queueGroup, cancellationToken);
            await using var channel = NatsServerGenerator<TContract>.CreateServerProxy(this, baseSubject, queueGroup, subscriptionId, serializer ?? Options.Serializer, contract, taskScheduler);
            _channels[subscriptionId] = channel;
            
            try
            {
                await channel.Listener(cancellationToken);
            }
            catch(OperationCanceledException)
            { }
        }
    }
}