namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading.Tasks;

    public class NatsSub : INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("SUB "));
        private static readonly ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool, NatsKey subject, NatsKey queueGroup, long subscriptionId)
        {
            Span<byte> subscriptionBytes = stackalloc byte[20]; // Max 20 - Uint64.MaxValue = 18446744073709551615 
            Utf8Formatter.TryFormat(subscriptionId, subscriptionBytes, out var subscriptionLength);
            subscriptionBytes = subscriptionBytes.Slice(0, subscriptionLength);

            var length = _command.Length;
            length += subject.Memory.Length + 1;
            length += queueGroup.Memory.Length>0? queueGroup.Memory.Length+1:0;
            length += subscriptionBytes.Length;
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
                consumed += queueGroup.Memory.Length;                                
                _del.CopyTo(buffer.Slice(consumed));
                consumed += _del.Length;
            }

            subscriptionBytes.CopyTo(buffer.Slice(consumed).Span);
            consumed += subscriptionBytes.Length;
            _end.CopyTo(buffer.Slice(consumed));
            
            
            return rented;
        }
    }
}