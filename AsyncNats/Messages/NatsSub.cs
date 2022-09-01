namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers.Text;
    using System.Text;

    public class NatsSub : INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("SUB "));
        private static readonly ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        public int Length => _length;

        private readonly int _length;
        private readonly NatsKey _subject;
        private readonly NatsKey _queueGroup;
        private readonly long _subscriptionId;
        private readonly ReadOnlyMemory<byte> _serialized;

        public NatsSub(NatsKey subject, NatsKey queueGroup, long subscriptionId)
        {
            _subject = subject;
            _queueGroup = queueGroup;
            _subscriptionId = subscriptionId;

            _serialized = Serialize(subject, queueGroup, subscriptionId);

            _length = _serialized.Length;
        }
        public void Serialize(Span<byte> buffer)
        {
            _serialized.Span.CopyTo(buffer);
        }

        public static ReadOnlyMemory<byte> Serialize(NatsKey subject, NatsKey queueGroup, long subscriptionId)
        {
            Span<byte> subscriptionBytes = stackalloc byte[20]; // Max 20 - Uint64.MaxValue = 18446744073709551615 
            Utf8Formatter.TryFormat(subscriptionId, subscriptionBytes, out var subscriptionLength);
            subscriptionBytes = subscriptionBytes.Slice(0, subscriptionLength);

            var length = _command.Length;
            length += subject.Memory.Length + 1;
            length += queueGroup.Memory.Length > 0 ? queueGroup.Memory.Length + 1 : 0;
            length += subscriptionBytes.Length;
            length += _end.Length;

            var buffer = new byte[length].AsMemory();

            _command.Span.CopyTo(buffer.Span);
            var consumed = _command.Length;

            subject.Memory.Span.CopyTo(buffer.Slice(consumed).Span);
            consumed += subject.Memory.Length;

            _del.Span.CopyTo(buffer.Slice(consumed).Span);
            consumed += _del.Length;
            if (!queueGroup.IsEmpty)
            {
                queueGroup.Memory.Span.CopyTo(buffer.Slice(consumed).Span);
                consumed += queueGroup.Memory.Length;
                _del.Span.CopyTo(buffer.Slice(consumed).Span);
                consumed += _del.Length;
            }

            subscriptionBytes.CopyTo(buffer.Slice(consumed).Span);
            consumed += subscriptionBytes.Length;
            _end.Span.CopyTo(buffer.Slice(consumed).Span);

            return buffer;
        }

        
    }
}