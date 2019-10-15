namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading.Tasks;

    public class NatsPing : INatsServerMessage, INatsClientMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("PING\r\n"));
        public async ValueTask Serialize(PipeWriter writer)
        {
            await writer.WriteAsync(_command);
        }

        public static INatsServerMessage? ParseMessage(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            return new NatsPing();
        }

        public static byte[] RentedSerialize()
        {
            var buffer = ArrayPool<byte>.Shared.Rent(_command.Length + 4);
            BitConverter.TryWriteBytes(buffer, _command.Length);
            _command.CopyTo(buffer.AsMemory(4));
            return buffer;
        }
    }
}