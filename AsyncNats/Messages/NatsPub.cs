namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Text;

    public class NatsPub
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("PUB "));
        private static readonly ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool, in NatsKey subject, in NatsKey replyTo, in NatsPayload payload)
        {
            var hint = _command.Length; // PUB
            hint += subject.Memory.Length + 1; // Subject + space
            hint += replyTo.IsEmpty ? 0 : replyTo.Memory.Length + 1; // ReplyTo
            if (payload.Memory.Length < 10) hint += 1;
            else if (payload.Memory.Length < 100) hint += 2;
            else if (payload.Memory.Length < 1_000) hint += 3;
            else if (payload.Memory.Length < 10_000) hint += 4;
            else if (payload.Memory.Length < 100_000) hint += 5;
            else if (payload.Memory.Length < 1_000_000) hint += 6;
            else if (payload.Memory.Length < 10_000_000) hint += 7;
            else throw new ArgumentOutOfRangeException(nameof(payload));

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
    }
}