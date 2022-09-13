namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Text;

    public class NatsPong : INatsServerMessage, INatsClientMessage
    {
        private static readonly NoOwner<byte> _command = new NoOwner<byte>(Encoding.UTF8.GetBytes("PONG\r\n"));

        public static readonly NatsPong Instance = new NatsPong();

        public static INatsServerMessage ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            return Instance;
        }

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool)
        {
            return _command;
        }

        public void Serialize(Span<byte> buffer)
        {
            _command.Memory.Span.CopyTo(buffer);
        }

        public int Length => _command.Memory.Length;
    }
}