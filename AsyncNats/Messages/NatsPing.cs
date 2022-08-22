namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading.Tasks;

    public class NatsPing : INatsServerMessage, INatsClientMessage
    {
        private static readonly NoOwner<byte> _command = new NoOwner<byte>(Encoding.UTF8.GetBytes("PING\r\n"));

        private static readonly NatsPing _instance = new NatsPing();
        
        public static INatsServerMessage? ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            return _instance;
        }

        public static IMemoryOwner<byte> RentedSerialize(NatsMemoryPool pool)
        {
            return _command; 
        }
    }
}