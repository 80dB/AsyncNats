namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Text;

    public class NatsPub
    {
        private static readonly  ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("PUB "));
        private static readonly  ReadOnlyMemory<byte> _del = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(" "));
        private static readonly  ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        public static byte[] RentedSerialize(string subject, string? replyTo, ReadOnlyMemory<byte> payload)
        {
            var hint = 4;
            hint += _command.Length; // PUB
            hint += subject.Length + 1; // Subject + space
            hint += replyTo?.Length + 1 ?? 0; // ReplyTo
            hint += 7; // Max payload size (1MB)
            hint += _end.Length; // Ending
            hint += payload.Length;
            hint += _end.Length; // Ending Payload

            var buffer = ArrayPool<byte>.Shared.Rent(hint);

            var consumed = 4;
            _command.CopyTo(buffer.AsMemory(consumed));
            consumed += _command.Length;
            consumed += Encoding.UTF8.GetBytes(subject, buffer.AsSpan(consumed));
            _del.CopyTo(buffer.AsMemory(consumed));
            consumed++;
            if (!string.IsNullOrEmpty(replyTo))
            {
                consumed += Encoding.UTF8.GetBytes(replyTo, buffer.AsSpan(consumed));
                _del.CopyTo(buffer.AsMemory(consumed));
                consumed++;
            }

            Utf8Formatter.TryFormat(payload.Length, buffer.AsSpan(consumed), out var written);
            consumed += written;
            _end.CopyTo(buffer.AsMemory(consumed));
            consumed += _end.Length;
            if (!payload.IsEmpty)
            {
                payload.CopyTo(buffer.AsMemory(consumed));
                consumed += payload.Length;
            }

            _end.CopyTo(buffer.AsMemory(consumed));
            consumed += _end.Length;

            BitConverter.TryWriteBytes(buffer, consumed-4);
            return buffer;
        }
    }
}