namespace InterfaceAsyncNatsSample
{
    using EightyDecibel.AsyncNats;
    using MessagePack;
    using System;

    public class NatsMessagePackSerializer : INatsSerializer
    {
        public T Deserialize<T>(ReadOnlyMemory<byte> buffer)
        {
            return MessagePackSerializer.Deserialize<T>(buffer);
        }

        public byte[] Serialize<T>(T obj)
        {
            return MessagePackSerializer.Serialize(obj);
        }
    }
}
