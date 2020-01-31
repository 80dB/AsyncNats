namespace EightyDecibel.AsyncNats.Rpc
{
    using EightyDecibel.AsyncNats.Channels;
    using EightyDecibel.AsyncNats.Messages;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    internal class NatsServerProxy<TContract> : INatsInternalChannel
    {
        protected readonly NatsConnection _parent;
        protected readonly Channel<INatsServerMessage> _channel;
        protected readonly INatsSerializer _serializer;
        protected readonly TContract _contract;

        public string? Subject { get; }
        public string? QueueGroup { get; }
        public string SubscriptionId { get; }

        private Dictionary<string, (InvokeAsyncDelegate invoke, SerializeDelegate serialize)> _asyncMethods;
        private Dictionary<string, InvokeDelegate> _syncMethods;

        internal NatsServerProxy(NatsConnection parent, string subject, string? queueGroup, string subscriptionId, INatsSerializer serializer, TContract contract, IReadOnlyDictionary<string, (MethodInfo invoke, MethodInfo serialize)> asyncMethods, IReadOnlyDictionary<string, MethodInfo> syncMethods)
        {
            _parent = parent;
            _channel = Channel.CreateBounded<INatsServerMessage>(parent.Options.ReceiverQueueLength);
            _serializer = serializer;
            _contract = contract;

            Subject = subject;
            QueueGroup = queueGroup;
            SubscriptionId = subscriptionId;

            _asyncMethods = new Dictionary<string, (InvokeAsyncDelegate invoke, SerializeDelegate serialize)>();
            foreach (var asyncMethod in asyncMethods)
            {
                var invoke = (InvokeAsyncDelegate)Delegate.CreateDelegate(typeof(InvokeAsyncDelegate), this, asyncMethod.Value.invoke);
                var serialize = (SerializeDelegate)Delegate.CreateDelegate(typeof(SerializeDelegate), this, asyncMethod.Value.serialize);
                _asyncMethods.Add(asyncMethod.Key, (invoke, serialize));
            }

            _syncMethods = new Dictionary<string, InvokeDelegate>();
            foreach (var syncMethod in syncMethods)
            {
                var invoke = (InvokeDelegate)Delegate.CreateDelegate(typeof(InvokeDelegate), this, syncMethod.Value);
                _syncMethods.Add(syncMethod.Key, invoke);
            }
        }

        public ValueTask DisposeAsync()
        {
            return _parent.Unsubscribe(this as INatsInternalChannel);
        }

        public ValueTask Publish(INatsServerMessage message)
        {
            if (message is NatsMsg msg) msg.Rent();
            return _channel.Writer.WriteAsync(message);
        }

        public async Task Listener(CancellationToken cancellationToken = default)
        {
            var reader = _channel.Reader;
            while (!cancellationToken.IsCancellationRequested)
            {
                INatsServerMessage message;
                try
                {
                    message = await reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                do
                {
                    if (!(message is NatsMsg msg)) continue;

                    try
                    {
                        var method = msg.Subject.Substring((Subject ?? string.Empty).Length - 1);
                        if (_asyncMethods.TryGetValue(method, out var delegates)) await InvokeAsync(delegates.invoke, delegates.serialize, msg);
                        else if (_syncMethods.TryGetValue(method, out var invoke)) await InvokeSync(invoke, msg);
                        else throw new KeyNotFoundException("Unknown method");
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch(Exception ex)
                    {
                        if (!string.IsNullOrEmpty(msg.ReplyTo))
                        {
                            using var ms = new MemoryStream();
                            var formatter = new BinaryFormatter();
                            formatter.Serialize(ms, ex);

                            await _parent.PublishObjectAsync(msg.ReplyTo, new NatsServerResponse { E = ms.ToArray() });
                        }
                    }
                    finally
                    {
                        msg.Release();
                    }
                } while (reader.TryRead(out message) && !cancellationToken.IsCancellationRequested);
            }
        }

        private async Task InvokeSync(InvokeDelegate invoke, NatsMsg msg)
        {
            var response = invoke(msg.Payload);
            if (!string.IsNullOrEmpty(msg.ReplyTo))
                await _parent.PublishAsync(msg.ReplyTo, response);
        }

        private async Task InvokeAsync(InvokeAsyncDelegate invoke, SerializeDelegate serialize, NatsMsg msg)
        {
            var task = invoke(msg.Payload);
            await task;
            var response = serialize(task);
            if (!string.IsNullOrEmpty(msg.ReplyTo))
                await _parent.PublishAsync(msg.ReplyTo, response);
        }
    }
}
