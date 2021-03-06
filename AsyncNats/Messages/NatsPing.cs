﻿namespace EightyDecibel.AsyncNats.Messages
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

        public static INatsServerMessage? ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            return new NatsPing();
        }

        public static IMemoryOwner<byte> RentedSerialize(MemoryPool<byte> pool)
        {
            var buffer = pool.Rent(_command.Length);
            _command.CopyTo(buffer.Memory);
            return buffer;
        }
    }
}