namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Text.Json;

    public class NatsDefaultSerializer : INatsSerializer
    {
        public byte[] Serialize<T>(T obj)
        {
            return JsonSerializer.SerializeToUtf8Bytes(obj);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> buffer)
        {
            return JsonSerializer.Deserialize<T>(buffer.Span);
        }
    }
}