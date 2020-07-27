namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;

    public class NatsOk : INatsServerMessage
    {
        public static INatsServerMessage? ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            return new NatsOk();
        }
    }
}