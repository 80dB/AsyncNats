namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Text;

    public class NatsUnsub : INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("UNSUB "));
        private static readonly ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        private readonly NatsKey _subscriptionId;
        private readonly int? _maxMessages;

        public NatsUnsub(long subscriptionId, int? maxMessages)
        {
            _maxMessages = maxMessages;
            _subscriptionId = subscriptionId.ToString();

            var length = _command.Length;
            length += _subscriptionId.Memory.Length;
            if (maxMessages.HasValue)
            {
                length += maxMessages.Value.CountDigits();
                length += _del.Length;
            }
            length += _end.Length;
            Length = length;
        }

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool, long subscriptionId, int? maxMessages)
        {
            Span<byte> subscriptionBytes = stackalloc byte[20]; // Max 20 - Uint64.MaxValue = 18446744073709551615 
            Utf8Formatter.TryFormat(subscriptionId, subscriptionBytes, out var subscriptionLength);
            subscriptionBytes = subscriptionBytes.Slice(0, subscriptionLength);

            var hint = _command.Length;
            hint += subscriptionBytes.Length;
            if (maxMessages != null)
            {
                if (maxMessages < 10) hint += 1;
                else if (maxMessages < 100) hint += 2;
                else if (maxMessages < 1_000) hint += 3;
                else if (maxMessages < 10_000) hint += 4;
                else if (maxMessages < 100_000) hint += 5;
                else if (maxMessages < 1_000_000) hint += 6;
                else if (maxMessages < 10_000_000) hint += 7;
                else throw new ArgumentOutOfRangeException(nameof(maxMessages));
                hint += _del.Length;
            }

            hint += _end.Length;

            var rented = pool.Rent(hint);
            var buffer = rented.Memory;
            _command.CopyTo(buffer);
            var consumed = _command.Length;

            subscriptionBytes.CopyTo(buffer.Slice(consumed).Span);
            consumed += subscriptionBytes.Length;
            if (maxMessages != null)
            {
                _del.CopyTo(buffer.Slice(consumed));
                consumed += _del.Length;
                Utf8Formatter.TryFormat(maxMessages.Value, buffer.Slice(consumed).Span, out var written);
                consumed += written;
            }

            _end.CopyTo(buffer.Slice(consumed));
            return rented;
        }

        public int Length { get; }

        public void Serialize(Span<byte> buffer)
        {
            _command.Span.CopyTo(buffer);
            var consumed = _command.Length;

            _subscriptionId.Memory.Span.CopyTo(buffer.Slice(consumed));
            consumed += _subscriptionId.Memory.Length;
            if (_maxMessages != null)
            {
                _del.Span.CopyTo(buffer.Slice(consumed));
                consumed += _del.Length;
                Utf8Formatter.TryFormat(_maxMessages.Value, buffer.Slice(consumed), out var written);
                consumed += written;
            }

            _end.Span.CopyTo(buffer.Slice(consumed));
        }
    }
}