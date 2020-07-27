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

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool, string subject, string? replyTo, ReadOnlyMemory<byte> payload)
        {
            var hint = _command.Length; // PUB
            hint += subject.Length + 1; // Subject + space
            hint += replyTo?.Length + 1 ?? 0; // ReplyTo
            if (payload.Length < 9) hint += 1;
            else if (payload.Length < 99) hint += 2;
            else if (payload.Length < 999) hint += 3;
            else if (payload.Length < 9_999) hint += 4;
            else if (payload.Length < 99_999) hint += 5;
            else if (payload.Length < 999_999) hint += 6;
            else if (payload.Length < 9_999_999) hint += 7;
            else throw new ArgumentOutOfRangeException(nameof(payload));

            hint += _end.Length; // Ending
            hint += payload.Length;
            hint += _end.Length; // Ending Payload

            var rented = pool.Rent(hint);
            var buffer = rented.Memory;

            _command.CopyTo(buffer);
            var consumed = _command.Length;
            consumed += Encoding.ASCII.GetBytes(subject, buffer.Slice(consumed).Span);
            _del.CopyTo(buffer.Slice(consumed));
            consumed++;
            if (!string.IsNullOrEmpty(replyTo))
            {
                consumed += Encoding.UTF8.GetBytes(replyTo, buffer.Slice(consumed).Span);
                _del.CopyTo(buffer.Slice(consumed));
                consumed++;
            }

            Utf8Formatter.TryFormat(payload.Length, buffer.Slice(consumed).Span, out var written);
            consumed += written;
            _end.CopyTo(buffer.Slice(consumed));
            consumed += _end.Length;
            if (!payload.IsEmpty)
            {
                payload.CopyTo(buffer.Slice(consumed));
                consumed += payload.Length;
            }

            _end.CopyTo(buffer.Slice(consumed));
            return rented;
        }
    }
}