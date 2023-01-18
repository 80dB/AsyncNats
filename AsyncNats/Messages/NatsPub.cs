namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers.Text;
    using System.Text;


    public readonly struct NatsPub: INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("PUB "));
        private static readonly ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        private readonly int _length;
        public int Length => _length;

        private readonly NatsKey _subject;
        private readonly NatsKey _replyTo;
        private readonly NatsPayload _payload;

        public NatsPub(in NatsKey subject, in NatsKey replyTo, in NatsPayload payload)
        {
            _subject = subject;
            _replyTo = replyTo;
            _payload = payload;

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

            _length = hint;
        }

        public void Serialize(Span<byte> buffer)
        {
            _command.Span.CopyTo(buffer);
            var consumed = _command.Length;
            _subject.Memory.Span.CopyTo(buffer.Slice(consumed));
            consumed += _subject.Memory.Length;
            _del.Span.CopyTo(buffer.Slice(consumed));
            consumed++;
            if (!_replyTo.IsEmpty)
            {
                _replyTo.Memory.Span.CopyTo(buffer.Slice(consumed));
                consumed += _replyTo.Memory.Length;
                _del.Span.CopyTo(buffer.Slice(consumed));
                consumed++;
            }

            Utf8Formatter.TryFormat(_payload.Memory.Length, buffer.Slice(consumed), out var written);
            consumed += written;
            _end.Span.CopyTo(buffer.Slice(consumed));
            consumed += _end.Length;
            if (!_payload.IsEmpty)
            {
                _payload.Memory.Span.CopyTo(buffer.Slice(consumed));
                consumed += _payload.Memory.Length;
            }

            _end.Span.CopyTo(buffer.Slice(consumed));
        }

        //test and debug only
        public static ReadOnlyMemory<byte> Serialize(in NatsKey subject, in NatsKey replyTo, in NatsPayload payload)
        {
            var pub = new NatsPub(subject, replyTo, payload);
            var buffer = new byte[pub.Length];
            pub.Serialize(buffer);

            return buffer;
        }
      
    }
}