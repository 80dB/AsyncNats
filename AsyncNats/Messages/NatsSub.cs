namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Text;

    public class NatsSub : INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = Encoding.UTF8.GetBytes("SUB ").AsMemory();
        private static readonly ReadOnlyMemory<byte> _del = Encoding.UTF8.GetBytes(" ").AsMemory();
        private static readonly ReadOnlyMemory<byte> _end = Encoding.UTF8.GetBytes("\r\n").AsMemory();

        private readonly NatsKey _subject;
        private readonly NatsKey _queueGroup;
        private readonly NatsKey _subscriptionId;

        public NatsSub(NatsKey subject, NatsKey queueGroup, long subscriptionId)
        {
            _subject = subject;
            _queueGroup = queueGroup;
            _subscriptionId = subscriptionId.ToString();

            var length = _command.Length;
            length += _subject.Memory.Length + 1;
            length += _queueGroup.Memory.Length > 0 ? _queueGroup.Memory.Length + 1 : 0;
            length += _subscriptionId.Memory.Length;
            length += _end.Length;
            Length = length;
        }

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

        public int Length { get; }

        public void Serialize(Span<byte> buffer)
        {
            _command.Span.CopyTo(buffer);
            var consumed = _command.Length;
            _subject.Memory.Span.CopyTo(buffer.Slice(consumed));
            consumed += _subject.Memory.Length;

            _del.Span.CopyTo(buffer.Slice(consumed));
            consumed += _del.Length;
            if (!_queueGroup.IsEmpty)
            {
                _queueGroup.Memory.Span.CopyTo(buffer.Slice(consumed));
                consumed += _queueGroup.Memory.Length;
                _del.Span.CopyTo(buffer.Slice(consumed));
                consumed += _del.Length;
            }

            _subscriptionId.Memory.Span.CopyTo(buffer.Slice(consumed));
            consumed += _subscriptionId.Memory.Length;
            _end.Span.CopyTo(buffer.Slice(consumed));
        }
    }
}