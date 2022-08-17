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

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool, Utf8String subject, Utf8String queueGroup, Utf8String subscriptionId)
        {
            var length = _command.Length;
            length += subject.Memory.Length + 1;
            length += queueGroup.Memory.Length>0? queueGroup.Memory.Length+1:0;
            length += subscriptionId.Memory.Length;
            length += _end.Length;

            var rented = pool.Rent(length);
            var buffer = rented.Memory;

            _command.CopyTo(buffer);
            var consumed = _command.Length;

            subject.Memory.Span.CopyTo(buffer.Slice(consumed).Span);
            consumed += subject.Memory.Length;

            _del.CopyTo(buffer.Slice(consumed));
            consumed += _del.Length;
            if (!queueGroup.IsEmpty)
            {
                queueGroup.Memory.Span.CopyTo(buffer.Slice(consumed).Span);
                consumed += subscriptionId.Memory.Length;                                
                _del.CopyTo(buffer.Slice(consumed));
                consumed += _del.Length;
            }

            subscriptionId.Memory.Span.CopyTo(buffer.Slice(consumed).Span);
            consumed += subscriptionId.Memory.Length;
            _end.CopyTo(buffer.Slice(consumed));
            
            return rented;
        }
    }
}