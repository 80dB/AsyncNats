namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading.Tasks;

    public class NatsPong : INatsServerMessage, INatsClientMessage
    {
        private static readonly NoOwner<byte> _command = new NoOwner<byte>(Encoding.UTF8.GetBytes("PONG\r\n"));

        private static readonly NatsPong _instance = new NatsPong();

        public static INatsServerMessage ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            return _instance;
        }

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool)
        {
            return _command;
        }
    }
}