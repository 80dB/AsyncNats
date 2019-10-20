namespace EightyDecibel.AsyncNats.Channels
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Messages;

    internal class NatsObjectChannel<T> : INatsObjectChannel<T>, INatsInternalChannel
    {
        private readonly NatsConnection _parent;
        private readonly Channel<INatsServerMessage> _channel;
        private readonly INatsSerializer _serializer;

        public string? Subject { get; }
        public string? QueueGroup { get; }
        public string SubscriptionId { get; }

        internal NatsObjectChannel(NatsConnection parent, string subject, string? queueGroup, string subscriptionId, INatsSerializer serializer)
        {
            _parent = parent;
            _channel = Channel.CreateBounded<INatsServerMessage>(parent.Options.ReceiverQueueLength);
            _serializer = serializer;

            Subject = subject;
            QueueGroup = queueGroup;
            SubscriptionId = subscriptionId;
        }

        public ValueTask Publish(INatsServerMessage message)
        {
            if (message is NatsMsg msg) msg.Rent();
            return _channel.Writer.WriteAsync(message);
        }

        public ValueTask DisposeAsync()
        {
            return _parent.Unsubscribe(this as INatsInternalChannel);
        }

        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default(CancellationToken))
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
                    yield break;
                }

                do
                {
                    if (!(message is NatsMsg msg)) continue;

                    try
                    {
                        yield return _serializer.Deserialize<T>(msg.Payload);
                    }
                    finally
                    {
                        msg.Release();
                    }
                } while (reader.TryRead(out message) && !cancellationToken.IsCancellationRequested);
            }
        }
    }
}