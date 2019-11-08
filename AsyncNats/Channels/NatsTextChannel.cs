namespace EightyDecibel.AsyncNats.Channels
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats.Messages;

    internal class NatsTextChannel : INatsChannel<string>, INatsInternalChannel
    {
        private readonly NatsConnection _parent;
        private readonly Channel<INatsServerMessage> _channel;

        public string? Subject { get; }
        public string? QueueGroup { get; }
        public string SubscriptionId { get; }

        internal NatsTextChannel(NatsConnection parent, string subject, string? queueGroup, string subscriptionId)
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
            return _parent.Unsubscribe(this as INatsInternalChannel);
        }

        public async IAsyncEnumerator<NatsTypedMsg<string>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
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
                        yield return new NatsTypedMsg<string>
                        {
                            Subject = msg.Subject,
                            ReplyTo = msg.ReplyTo,
                            SubscriptionId = msg.SubscriptionId,
                            Payload = Encoding.UTF8.GetString(msg.Payload.Span)
                        };
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