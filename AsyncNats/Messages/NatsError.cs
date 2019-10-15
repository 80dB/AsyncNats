namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Text;

    public class NatsError : INatsServerMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("-ERR '"));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("'\r\n"));

        public string? Error { get; set; }

        public static INatsServerMessage? ParseMessage(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            if (line.Length == 4) return new NatsError();
            var error = line.Slice(6, line.Length - 7); // Remove "-ERR ''"
            return new NatsError {Error = Encoding.UTF8.GetString(error)};
        }
    }
}