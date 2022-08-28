namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading.Tasks;

    public class NatsPong : INatsClientMessage,INatsServerMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = Encoding.UTF8.GetBytes("PONG\r\n");

        public static readonly NatsPong Instance = new NatsPong();

        public int Length => _command.Length;

        public static INatsServerMessage ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            return Instance;
        }

        public void Serialize(Span<byte> buffer)
        {
            _command.Span.CopyTo(buffer);
        }

        public static ReadOnlyMemory<byte> Serialize()
        {
            return _command;
        }
    }
}