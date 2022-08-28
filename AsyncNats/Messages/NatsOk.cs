namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;

    public class NatsOk : INatsServerMessage
    {
        private readonly static NatsOk _instance = new NatsOk();

        public static INatsServerMessage? ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            return _instance;
        }
    }

    
}