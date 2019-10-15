namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading.Tasks;

    public class NatsSub : INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("SUB "));
        private static readonly ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        public string Subject { get; set; } = string.Empty;
        public string? QueueGroup { get; set; }
        public string SubscriptionId { get; set; } = string.Empty;

        public async ValueTask Serialize(PipeWriter writer)
        {
            await writer.WriteAsync(Encoding.UTF8.GetBytes($"SUB {Subject} {(string.IsNullOrEmpty(QueueGroup) ? "" : $"{QueueGroup} ")}{SubscriptionId}\r\n"));
        }

        public static byte[] RentedSerialize(string subject, string? queueGroup, string subscriptionId)
        {
            var length = 4;
            length += _command.Length;
            length += subject.Length + 1;
            length += queueGroup?.Length + 1 ?? 0;
            length += subscriptionId.Length;
            length += _end.Length;

            var buffer = ArrayPool<byte>.Shared.Rent(length);

            var consumed = 4;
            _command.CopyTo(buffer.AsMemory(consumed));
            consumed += _command.Length;
            consumed += Encoding.UTF8.GetBytes(subject, buffer.AsSpan(consumed));
            _del.CopyTo(buffer.AsMemory(consumed));
            consumed += _del.Length;
            if (!string.IsNullOrEmpty(queueGroup))
            {
                consumed += Encoding.UTF8.GetBytes(queueGroup, buffer.AsSpan(consumed));
                _del.CopyTo(buffer.AsMemory(consumed));
                consumed += _del.Length;
            }
            consumed += Encoding.UTF8.GetBytes(subscriptionId, buffer.AsSpan(consumed));
            _end.CopyTo(buffer.AsMemory(consumed));
            consumed += _end.Length;

            BitConverter.TryWriteBytes(buffer, consumed-4);
            return buffer;
        }
    }
}