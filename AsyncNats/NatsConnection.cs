namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO.Pipelines;
    using System.Linq;
    using System.Net.Sockets;
    using System.Runtime.InteropServices.ComTypes;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Channels;
    using EightyDecibel.AsyncNats.Messages;

    public class NatsConnection : IAsyncDisposable
    {
        private static long _nextSubscriptionId = 1;

        private Task? _readWriteAsyncTask;
        private CancellationTokenSource? _disconnectSource;
        private Channel<byte[]> _senderChannel;
        private Channel<INatsServerMessage> _receiverChannel;
        private ConcurrentDictionary<string, INatsChannel> _channels;

        private Task _dispatchTask;
        private CancellationTokenSource _disposeTokenSource;

        public INatsOptions Options { get; }

        public NatsConnection()
            : this(new NatsOptions())
        { }

        public NatsConnection(INatsOptions options)
        {
            Options = options;

            _senderChannel = Channel.CreateBounded<byte[]>(Options.SenderQueueLength);
            _receiverChannel = Channel.CreateBounded<INatsServerMessage>(Options.ReceiverQueueLength);
            _channels = new ConcurrentDictionary<string, INatsChannel>();
            _disposeTokenSource= new CancellationTokenSource();
            _dispatchTask = Dispatcher(_disposeTokenSource.Token);
        }

        public Task ConnectAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested) throw new ObjectDisposedException("Connection already disposed");
            if (_disconnectSource != null) throw new InvalidAsynchronousStateException("Already connected");

            _disconnectSource = new CancellationTokenSource();
            _readWriteAsyncTask = ReadWriteAsync(_disconnectSource.Token);
            return Task.CompletedTask;
        }

        private async Task ReadWriteAsync(CancellationToken disconnectToken)
        {
            while (!disconnectToken.IsCancellationRequested)
            {
                using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.NoDelay = true;
                
                using var internalDisconnect = new CancellationTokenSource();

                try
                {
                    await socket.ConnectAsync(Options.Server);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Exception {ex}");
                    await Task.Delay(TimeSpan.FromSeconds(1), disconnectToken);
                    continue;
                }

                var readPipe = new Pipe(Options.ReceiverPipeOptions);
                var readTask = ReadSocketAsync(socket, readPipe.Writer, internalDisconnect.Token);
                var processTask = ProcessMessagesAsync(readPipe.Reader, internalDisconnect.Token);

                var writePipe = new Pipe(Options.SenderPipeOptions);
                var serializeTask = FillSenderPipeAsync(writePipe.Writer, internalDisconnect.Token);
                var writeTask = WriteSocketAsync(socket, writePipe.Reader, internalDisconnect.Token);
                try
                {
                    Task.WaitAny(new[] {readTask, processTask, serializeTask, writeTask}, disconnectToken);
                }
                catch(Exception ex)
                {
                    Debug.WriteLine($"Exception {ex}");
                    // Not sure what can happen here, but at least stop after we catch it :)
                }

                internalDisconnect.Cancel();
                await WaitAll(readTask, processTask, serializeTask, writeTask);
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
                { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Unhandled exception {ex}");
                    // Ignore (maybe log?)
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

                var flush = await writer.FlushAsync(disconnectToken);
                if (flush.IsCompleted || flush.IsCanceled) break;
            }
        }

        private async Task ProcessMessagesAsync(PipeReader reader, CancellationToken disconnectToken)
        {
            var writer = _receiverChannel.Writer;
            var parser = new NatsMessageParser();
            while (!disconnectToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(disconnectToken);
                if (read.IsCanceled) break;
                do
                {
                    var messages = parser.ParseMessages(read.Buffer, out var consumed);
                    reader.AdvanceTo(read.Buffer.GetPosition(consumed));

                    foreach (var message in messages)
                        await writer.WriteAsync(message, disconnectToken);
                } while (reader.TryRead(out read));
            }
        }

        private async Task FillSenderPipeAsync(PipeWriter writer, CancellationToken disconnectToken)
        {
            var reader = _senderChannel.Reader;
            while (!disconnectToken.IsCancellationRequested)
            {
                var buffer = await reader.ReadAsync(disconnectToken);
                var count = 0;
                do
                {
                    var consumed = BitConverter.ToInt32(buffer);
                    var memory = writer.GetMemory(consumed);
                    buffer.AsMemory(4, consumed).CopyTo(memory);
                    writer.Advance(consumed);
                    ArrayPool<byte>.Shared.Return(buffer);
                    
                    if (!reader.TryRead(out buffer)) break;
                    count++;
                } while (count < Options.FlushAtLeastEvery);

                await writer.FlushAsync(disconnectToken);
            }
        }

        private async Task WriteSocketAsync(Socket socket, PipeReader reader, CancellationToken disconnectToken)
        {
            await SendConnect(socket, disconnectToken);
            await Resubscribe(socket, disconnectToken);
            while (!disconnectToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(disconnectToken);
                do
                {
                    foreach (var segment in read.Buffer)
                    {
                        await socket.SendAsync(segment, SocketFlags.None, disconnectToken);
                    }

                    reader.AdvanceTo(read.Buffer.End);
                } while (reader.TryRead(out read));
            }
        }

        private async Task SendConnect(Socket socket, CancellationToken disconnectToken)
        {
            var connect = new NatsConnect();
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

        private async Task Dispatcher(CancellationToken disconnectToken)
        {
            var reader = _receiverChannel.Reader;
            var writer = _senderChannel.Writer;
            while (!disconnectToken.IsCancellationRequested)
            {
                var message = await reader.ReadAsync(disconnectToken);
                do
                {
                    if (message is NatsPing)
                    {
                        await writer.WriteAsync(NatsPong.RentedSerialize(), disconnectToken);
                    }

                    var msg = message as NatsMsg;
                    var subscriptionId = msg?.SubscriptionId;
                    foreach (var channel in _channels.Values)
                    {
                        if (subscriptionId != null && subscriptionId != channel.SubscriptionId) continue;
                        await channel.Publish(message);
                    }

                    msg?.Release();
                } while (reader.TryRead(out message));
            }
        }

        public async Task DisconnectAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested) throw new ObjectDisposedException("Connection already disposed");
            try
            {
                // Empty output channel
                while (_senderChannel.Reader.TryRead(out var dummy))
                { }

                _disconnectSource?.Cancel();
                if (_readWriteAsyncTask != null)
                    await _readWriteAsyncTask;
            }
            catch
            {
                // Ignore any disconnect issues
            }
            finally
            {
                _disconnectSource?.Dispose();
                _readWriteAsyncTask = null;
                _disconnectSource = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested) throw new ObjectDisposedException("Connection already disposed");

            foreach (var channel in _channels.Values.ToArray())
                await channel.DisposeAsync();

            _disposeTokenSource.Cancel();
            await DisconnectAsync();
            await _dispatchTask;
            _disposeTokenSource.Dispose();
        }

        public ValueTask PublishAsync<T>(string subject, T payload, string? replyTo = null)
        {
            return PublishAsync(subject, Options.Serializer.Serialize(payload), replyTo);
        }

        public async ValueTask PublishAsync(string subject, byte[]? payload, string? replyTo = null)
        {
            var pub = NatsPub.RentedSerialize(subject, replyTo, payload);
            if (!_senderChannel.Writer.TryWrite(pub))
                await _senderChannel.Writer.WriteAsync(pub);
        }

        public IAsyncEnumerable<INatsServerMessage> SubscribeAll()
        {
            var subscriptionId = Interlocked.Increment(ref _nextSubscriptionId).ToString();
            var channel = new NatsUntypedChannel(this, null, null, subscriptionId);
            _channels[subscriptionId] = channel;
            return channel;
        }

        public async ValueTask<IAsyncEnumerable<INatsServerMessage>> Subscribe(string subject, string? queueGroup = null)
        {
            var subscriptionId = Interlocked.Increment(ref _nextSubscriptionId).ToString();
            await _senderChannel.Writer.WriteAsync(NatsSub.RentedSerialize(subject, queueGroup, subscriptionId));
            var channel = new NatsUntypedChannel(this, subject, queueGroup, subscriptionId);
            _channels[subscriptionId] = channel;
            return channel;
        }

        public async ValueTask<IAsyncEnumerable<NatsTypedMsg<T>>> Subscribe<T>(string subject, string? queueGroup = null, INatsSerializer? serializer = null) where T : class
        {
            var subscriptionId = Interlocked.Increment(ref _nextSubscriptionId).ToString();
            await _senderChannel.Writer.WriteAsync(NatsSub.RentedSerialize(subject, queueGroup, subscriptionId));
            var channel = new NatsTypedChannel<T>(this, subject, queueGroup, subscriptionId, serializer ?? Options.Serializer);
            _channels[subscriptionId] = channel;
            return channel;
        }

        internal async ValueTask Unsubscribe(INatsChannel channel)
        {
            if (!_channels.TryRemove(channel.SubscriptionId, out var dummy)) return;
            if (string.IsNullOrEmpty(channel.Subject)) return;

            await _senderChannel.Writer.WriteAsync(NatsUnsub.RentedSerialize(channel.SubscriptionId, null));
        }
    }
}