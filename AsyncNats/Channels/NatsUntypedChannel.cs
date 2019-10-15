namespace EightyDecibel.AsyncNats.Channels
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Messages;

    internal class NatsUntypedChannel : IAsyncEnumerable<INatsServerMessage>, INatsChannel
    {
        private readonly NatsConnection _parent;
        private readonly Channel<INatsServerMessage> _channel;

        public string? Subject { get; set; }
        public string? QueueGroup { get; set; }
        public string SubscriptionId { get; set; }

        internal NatsUntypedChannel(NatsConnection parent, string? subject, string? queueGroup, string subscriptionId)
        {
            _parent = parent;
            _channel = Channel.CreateBounded<INatsServerMessage>(parent.Options.ReceiverQueueLength);
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
            return _parent.Unsubscribe(this);
        }

        public async IAsyncEnumerator<INatsServerMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default(CancellationToken))
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
                    if (message is NatsMsg msg)
                    {
                        try
                        {
                            var payload = new byte[msg.Payload.Length];
                            msg.Payload.CopyTo(payload.AsMemory());
                            yield return new NatsMsg
                            {
                                Payload = payload,
                                ReplyTo = msg.ReplyTo,
                                SubscriptionId = msg.SubscriptionId,
                                Subject = msg.SubscriptionId
                            };
                        }
                        finally
                        {
                            msg.Release();
                        }
                    }
                    else
                    {
                        yield return message;
                    }
                } while (reader.TryRead(out message));
            }
        }
    }
}