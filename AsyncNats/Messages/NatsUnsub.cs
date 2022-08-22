namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    public class NatsUnsub : INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("UNSUB "));
        private static readonly ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        public string SubscriptionId { get; set; } = string.Empty;
        public int MaxMessages { get; set; }

        public async ValueTask Serialize(PipeWriter writer)
        {
            await writer.WriteAsync(Encoding.UTF8.GetBytes($"UNSUB {SubscriptionId} {MaxMessages}\r\n"));
        }

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool, long subscriptionId, int? maxMessages)
        {
            byte[] subscriptionBytes = Encoding.UTF8.GetBytes(subscriptionId.ToString());

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
    }
}