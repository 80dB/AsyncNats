namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Net;
    using System.Text;
    using System.Threading;

    public class NatsMsg : INatsServerMessage
    {
        private static readonly byte[] _empty = new byte[0];
        private int _referenceCounter = 0;
        private byte[] _rentedPayload = _empty;

        public string Subject { get; set; } = string.Empty;
        public string SubscriptionId { get; set; } = string.Empty;
        public string ReplyTo { get; set; } = string.Empty;
        public ReadOnlyMemory<byte> Payload { get; set; } = _empty;

        public void Rent()
        {
            Interlocked.Increment(ref _referenceCounter);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _referenceCounter) == 0)
                ArrayPool<byte>.Shared.Return(_rentedPayload);
        }

        public static INatsServerMessage? ParseMessage(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            var next = line.Slice(4); // Remove "MSG "
            var part = next.Slice(0, next.IndexOf((byte)' '));
            var subject = Encoding.ASCII.GetString(part);

            next = next.Slice(part.Length + 1);
            part = next.Slice(0, next.IndexOf((byte)' '));
            var sid = Encoding.ASCII.GetString(part);

            next = next.Slice(part.Length + 1);
            part = next;
            var replyTo = string.Empty;
            var split = next.IndexOf((byte)' ');
            if (split > -1)
            {
                part = next.Slice(0, split);
                replyTo = Encoding.ASCII.GetString(part);
                part = next.Slice(part.Length + 1);
            }
            if (!Utf8Parser.TryParse(part, out int payloadSize, out int consumed)) throw new ProtocolViolationException($"Invalid message payload size {Encoding.ASCII.GetString(line)}");

            if (reader.Remaining < payloadSize + 2) return null;
            
            var payload = ArrayPool<byte>.Shared.Rent(payloadSize);
            reader.Sequence.Slice(reader.Position, payloadSize).CopyTo(payload);
            reader.Advance(payloadSize + 2);
            return new NatsMsg { Subject = subject, Payload = payload.AsMemory(0, payloadSize), _rentedPayload = payload, ReplyTo = replyTo, SubscriptionId = sid, _referenceCounter = 1 };
        }
    }
}