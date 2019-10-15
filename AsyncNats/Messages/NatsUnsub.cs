namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading.Tasks;

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

        public static byte[] RentedSerialize(string subscriptionId, int? maxMessages)
        {
            var length = 4;
            length += _command.Length;
            length += subscriptionId.Length;
            if (maxMessages != null)
            {
                length += 7;
                length += _del.Length;
            }

            length += _end.Length;

            var buffer = ArrayPool<byte>.Shared.Rent(length);
            var consumed = 4;
            _command.CopyTo(buffer.AsMemory(consumed));
            consumed += _command.Length;
            consumed += Encoding.UTF8.GetBytes(subscriptionId, buffer.AsSpan(consumed));
            if (maxMessages != null)
            {
                _del.CopyTo(buffer.AsMemory(consumed));
                consumed += _del.Length;
                Utf8Formatter.TryFormat(maxMessages.Value, buffer.AsSpan(consumed), out var written);
                consumed += written;
            }
            _end.CopyTo(buffer.AsMemory(consumed));
            consumed += _end.Length;

            BitConverter.TryWriteBytes(buffer, consumed-4);
            return buffer;
        }
    }
}