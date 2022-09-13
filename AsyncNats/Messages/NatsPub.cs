namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Text;

    public struct NatsPub : INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = Encoding.UTF8.GetBytes("PUB ").AsMemory();
        private static readonly ReadOnlyMemory<byte> _del = Encoding.UTF8.GetBytes(" ").AsMemory();
        private static readonly ReadOnlyMemory<byte> _end = Encoding.UTF8.GetBytes("\r\n").AsMemory();

        private readonly NatsKey _subject;
        private readonly NatsKey _replyTo;
        private readonly NatsPayload _payload;

        public NatsPub(in NatsKey subject, in NatsKey replyTo, in NatsPayload payload)
        {
            _subject = subject;
            _replyTo = replyTo;
            _payload = payload;

            var length = _command.Length; // PUB
            length += subject.Memory.Length + 1; // Subject + space
            length += replyTo.IsEmpty ? 0 : replyTo.Memory.Length + 1; // ReplyTo
            length += payload.Memory.Length.CountDigits();
            length += _end.Length; // Ending
            length += payload.Memory.Length;
            length += _end.Length; // Ending Payload
            Length = length;
        }

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool, in NatsKey subject, in NatsKey replyTo, in NatsPayload payload)
        {
            var hint = _command.Length; // PUB
            hint += subject.Memory.Length + 1; // Subject + space
            hint += replyTo.IsEmpty ? 0 : replyTo.Memory.Length + 1; // ReplyTo
            hint += payload.Memory.Length.CountDigits();
            hint += _end.Length; // Ending
            hint += payload.Memory.Length;
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

            Utf8Formatter.TryFormat(payload.Memory.Length, buffer.Slice(consumed).Span, out var written);
            consumed += written;
            _end.CopyTo(buffer.Slice(consumed));
            consumed += _end.Length;
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

            Utf8Formatter.TryFormat(_payload.Memory.Length, buffer.Slice(consumed), out var written);
            consumed += written;
            _end.Span.CopyTo(buffer.Slice(consumed));
            consumed += _end.Length;
            if (!_payload.IsEmpty)
            {
                _payload.Memory.Span.CopyTo(buffer.Slice(consumed));
                consumed += _payload.Memory.Length;
            }

            _end.Span.CopyTo(buffer.Slice(consumed));
        }

        public int Length { get; private set; }
    }
}