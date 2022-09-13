namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Text;


    public class NatsHPub : INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("HPUB "));
        private static readonly ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        private readonly NatsKey _subject;
        private readonly NatsKey _replyTo;
        private readonly NatsPayload _payload;
        private readonly NatsMsgHeaders _header;

        public NatsHPub(in NatsKey subject, in NatsKey replyTo, in NatsMsgHeaders header, in NatsPayload payload)
        {
            _subject = subject;
            _replyTo = replyTo;
            _payload = payload;
            _header = header;

            var length = _command.Length; // PUB
            length += subject.Memory.Length + 2; // Subject + space
            length += replyTo.IsEmpty ? 0 : replyTo.Memory.Length + 1; // ReplyTo
            length += header.SerializedLength.CountDigits();
            length += (header.SerializedLength + payload.Memory.Length).CountDigits();
            length += _end.Length; // Ending
            length += payload.Memory.Length;
            length += header.SerializedLength;
            length += _end.Length; // Ending Payload
            Length = length;
        }

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool, in NatsKey subject, in NatsKey replyTo, in NatsMsgHeaders header, in NatsPayload payload)
        {
            var totalLength = payload.Memory.Length + header.SerializedLength;
            
            var hint = _command.Length; // HPUB
            hint += subject.Memory.Length + 2; // Subject + spaces
            hint += replyTo.IsEmpty ? 0 : replyTo.Memory.Length + 1; // ReplyTo
            
            if (header.SerializedLength < 10) hint += 1;
            else if (header.SerializedLength < 100) hint += 2;
            else if (header.SerializedLength < 1_000) hint += 3;
            else if (header.SerializedLength < 10_000) hint += 4;
            else if (header.SerializedLength < 100_000) hint += 5;
            else if (header.SerializedLength < 1_000_000) hint += 6;
            else if (header.SerializedLength < 10_000_000) hint += 7;
            else throw new ArgumentOutOfRangeException(nameof(header));
            
            if (totalLength < 10) hint += 1;
            else if (totalLength < 100) hint += 2;
            else if (totalLength < 1_000) hint += 3;
            else if (totalLength < 10_000) hint += 4;
            else if (totalLength < 100_000) hint += 5;
            else if (totalLength < 1_000_000) hint += 6;
            else if (totalLength < 10_000_000) hint += 7;
            else throw new ArgumentOutOfRangeException(nameof(payload));
            
            

            hint += _end.Length; // Ending
            hint += payload.Memory.Length;
            hint += header.SerializedLength;
            hint += _end.Length; // Ending Payload

            var rented = pool.Rent(hint);
            var buffer = rented.Memory;

            _command.CopyTo(buffer);
            var consumed = _command.Length;
            subject.Memory.Span.CopyTo(buffer.Slice(consumed).Span);
            consumed += subject.Memory.Length;
            _del.CopyTo(buffer.Slice(consumed));
            consumed++;
            if (!replyTo.IsEmpty)
            {
                replyTo.Memory.Span.CopyTo(buffer.Slice(consumed).Span);
                consumed += replyTo.Memory.Length;
                _del.CopyTo(buffer.Slice(consumed));
                consumed++;
            }

            Utf8Formatter.TryFormat(header.SerializedLength, buffer.Slice(consumed).Span, out var written);
            consumed += written;
            _del.CopyTo(buffer.Slice(consumed));
            consumed++;

            Utf8Formatter.TryFormat(payload.Memory.Length + header.SerializedLength, buffer.Slice(consumed).Span, out written);            
            consumed += written;
            _end.CopyTo(buffer.Slice(consumed));
            consumed += _end.Length;

            header.SerializeTo(buffer.Slice(consumed).Span);
            consumed += header.SerializedLength;

            if (!payload.IsEmpty)
            {
                payload.Memory.CopyTo(buffer.Slice(consumed));
                consumed += payload.Memory.Length;
            }

            _end.CopyTo(buffer.Slice(consumed));
            return rented;
        }

        public void Serialize(Span<byte> buffer)
        {
            _command.Span.CopyTo(buffer);
            var consumed = _command.Length;
            _subject.Memory.Span.CopyTo(buffer.Slice(consumed));
            consumed += _subject.Memory.Length;
            _del.Span.CopyTo(buffer.Slice(consumed));
            consumed++;
            if (!_replyTo.IsEmpty)
            {
                _replyTo.Memory.Span.CopyTo(buffer.Slice(consumed));
                consumed += _replyTo.Memory.Length;
                _del.Span.CopyTo(buffer.Slice(consumed));
                consumed++;
            }

            Utf8Formatter.TryFormat(_header.SerializedLength, buffer.Slice(consumed), out var written);
            consumed += written;
            _del.Span.CopyTo(buffer.Slice(consumed));
            consumed++;
            
            Utf8Formatter.TryFormat(_payload.Memory.Length + _header.SerializedLength, buffer.Slice(consumed), out written);
            consumed += written;
            _end.Span.CopyTo(buffer.Slice(consumed));
            consumed += _end.Length;

            _header.SerializeTo(buffer.Slice(consumed));
            consumed += _header.SerializedLength;

            if (!_payload.IsEmpty)
            {
                _payload.Memory.Span.CopyTo(buffer.Slice(consumed));
                consumed += _payload.Memory.Length;
            }

            _end.Span.CopyTo(buffer.Slice(consumed));
        }

        public int Length { get; }
    }
}