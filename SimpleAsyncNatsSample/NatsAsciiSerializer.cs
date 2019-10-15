namespace SimpleAsyncNatsSample
{
    using System;
    using System.Text;
    using EightyDecibel.AsyncNats;

    public class NatsAsciiSerializer : INatsSerializer
    {
        public byte[] Serialize<T>(T obj)
        {
            return Encoding.ASCII.GetBytes(obj as string);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> buffer)
        {
            if (typeof(T) != typeof(string)) throw new Exception("You are not supposed to throw anything other than strings at me");
            return (T)(object)Encoding.ASCII.GetString(buffer.Span);
        }
    }
}