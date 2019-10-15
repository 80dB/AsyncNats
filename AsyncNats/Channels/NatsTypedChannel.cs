namespace EightyDecibel.AsyncNats.Channels
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Messages;

    internal class NatsTypedChannel<T> : IAsyncEnumerable<NatsTypedMsg<T>>, INatsChannel where T : class
    {
        private readonly NatsConnection _parent;
        private readonly Channel<INatsServerMessage> _channel;
        private readonly INatsSerializer _serializer;

        public string? Subject { get; set; }
        public string? QueueGroup { get; set; }
        public string SubscriptionId { get; set; }

        internal NatsTypedChannel(NatsConnection parent, string subject, string? queueGroup, string subscriptionId, INatsSerializer serializer)
        {
            _parent = parent;
            _channel = Channel.CreateBounded<INatsServerMessage>(parent.Options.ReceiverQueueLength);
            Subject = subject;
            QueueGroup = queueGroup;
            SubscriptionId = subscriptionId;
            _serializer = serializer;
        }

        public ValueTask Publish(INatsServerMessage message)
        {
            if (message is NatsMsg msg) msg.Rent();
            return _channel.Writer.WriteAsync(message);
        }

        public ValueTask DisposeAsync()
        {
            return _parent.Unsubscribe(this);
        }

        public async IAsyncEnumerator<NatsTypedMsg<T>> GetAsyncEnumerator(CancellationToken cancellationToken = default(CancellationToken))
        {
            var reader = _channel.Reader;
            while (!cancellationToken.IsCancellationRequested)
            {
                INatsServerMessage message;
                try
                {
                    message = await reader.ReadAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Exception {ex}");
                    yield break;
                }

                do
                {
                    if (!(message is NatsMsg msg)) continue;

                    try
                    {
                        yield return new NatsTypedMsg<T>
                        {
                            Subject = msg.Subject,
                            ReplyTo = msg.ReplyTo,
                            SubscriptionId = msg.SubscriptionId,
                            Payload = _serializer.Deserialize<T>(msg.Payload)
                        };
                    }
                    finally
                    {
                        msg.Release();
                    }
                } while (reader.TryRead(out message));
            }
        }
    }
}