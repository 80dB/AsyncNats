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
        private int _referenceCounter;
        private IMemoryOwner<byte>? _rentedPayload;

        private ReadOnlyMemory<byte> _headerMemory;
        private NatsMsgHeaders? _headers;
        
        public string Subject { get; set; } = string.Empty;
        public string SubscriptionId { get; set; } = string.Empty;
        public string ReplyTo { get; set; } = string.Empty;
        public ReadOnlyMemory<byte> Payload { get; set; } = _empty;
        
        public NatsMsgHeaders Headers
        {
            get
            {                                
                if (_headers != null)
                    return _headers;

                if (_headerMemory.IsEmpty == false)
                    _headers = new NatsMsgHeaders(_headerMemory);
                else
                    _headers = NatsMsgHeaders.Empty;

                return _headers;
            }
        }

        public void Rent()
        {
            Interlocked.Increment(ref _referenceCounter);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _referenceCounter) == 0)
            {
                _rentedPayload?.Dispose();
                _rentedPayload = null;
            }
        }

        public static INatsServerMessage? ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            var next = line.Slice(4); // Remove "MSG "
            var part = next.Slice(0, next.IndexOf((byte) ' '));
            var subject = Encoding.ASCII.GetString(part);

            next = next.Slice(part.Length + 1);
            part = next.Slice(0, next.IndexOf((byte) ' '));
            var sid = Encoding.ASCII.GetString(part);

            next = next.Slice(part.Length + 1);
            part = next;
            var replyTo = string.Empty;
            var split = next.IndexOf((byte) ' ');
            if (split > -1)
            {
                part = next.Slice(0, split);
                replyTo = Encoding.ASCII.GetString(part);
                part = next.Slice(part.Length + 1);
            }

            if (!Utf8Parser.TryParse(part, out int payloadSize, out int consumed)) throw new ProtocolViolationException($"Invalid message payload size {Encoding.ASCII.GetString(line)}");

            if (reader.Remaining < payloadSize + 2) return null;

            var payload = pool.Rent(payloadSize);
            reader.Sequence.Slice(reader.Position, payloadSize).CopyTo(payload.Memory.Span);
            reader.Advance(payloadSize + 2);
            return new NatsMsg {Subject = subject, Payload = payload.Memory, _headerMemory=ReadOnlyMemory<byte>.Empty, _rentedPayload = payload, ReplyTo = replyTo, SubscriptionId = sid, _referenceCounter = 1};
        }

        public static INatsServerMessage? ParseMessageWithHeader(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            var next = line.Slice(5); // Remove "HMSG "
            var part = next.Slice(0, next.IndexOf((byte)' '));
            var subject = Encoding.ASCII.GetString(part);

            next = next.Slice(part.Length + 1);
            part = next.Slice(0, next.IndexOf((byte)' '));
            var sid = Encoding.ASCII.GetString(part);

            next = next.Slice(part.Length + 1);
            part = next;
            var replyTo = string.Empty;

            var splitCount = 0;
            for (var i=0;i<next.Length;i++)
            {
                if (next[i] == ' ') splitCount++;
            }

            int headerSize = 0;
            int totalSize = 0;
            int consumed = 0;

            if (splitCount == 2)
            {
                var split = next.IndexOf((byte)' ');
                part = next.Slice(0, split);
                replyTo = Encoding.ASCII.GetString(part);
                part = next.Slice(part.Length + 1);
            }
            else if(splitCount > 2)
            {
                throw new ProtocolViolationException($"Invalid message");
            }

            if (!Utf8Parser.TryParse(part, out headerSize, out consumed)) throw new ProtocolViolationException($"Invalid message header size {Encoding.ASCII.GetString(line)}");
            part = next.Slice(consumed + 1);

            if (!Utf8Parser.TryParse(part, out totalSize, out consumed)) throw new ProtocolViolationException($"Invalid message total size {Encoding.ASCII.GetString(line)}");

            var payloadSize = totalSize - headerSize;

            if (reader.Remaining < totalSize + 2) return null;


            var payloadAndHeader = pool.Rent(totalSize);
            
            reader.Sequence.Slice(reader.Position, headerSize).CopyTo(payloadAndHeader.Memory.Span);            
            reader.Advance(headerSize);
            var header = payloadAndHeader.Memory.Slice(0, headerSize);

            if (payloadSize > 0)
            {                
                reader.Sequence.Slice(reader.Position, payloadSize).CopyTo(payloadAndHeader.Memory.Span.Slice(headerSize));
                reader.Advance(payloadSize + 2);
                var payload = payloadAndHeader.Memory.Slice(headerSize);

                return new NatsMsg { Subject = subject, Payload = payload, _rentedPayload = payloadAndHeader, _headerMemory = header, ReplyTo = replyTo, SubscriptionId = sid, _referenceCounter = 1 };

            }
            else
                return new NatsMsg { Subject = subject, _rentedPayload = payloadAndHeader, _headerMemory=header, ReplyTo = replyTo, SubscriptionId = sid, _referenceCounter = 1 };


        }

    }
}