namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class NatsConnect
    {
        private static readonly  ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("CONNECT "));
        private static readonly  ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        [JsonPropertyName("verbose")]
        public bool Verbose { get; set; }
        [JsonPropertyName("pedantic")]
        public bool Pedantic { get; set; }
        [JsonPropertyName("tls_required")]
        public bool TlsRequired { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("lang")]
        public string Lang { get; set; } = "C#";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "0.0.1";

        [JsonPropertyName("protocol")]
        public int Protocol { get; set; } = 1;

        public static byte[] RentedSerialize(NatsConnect msg)
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(msg);
            var buffer = ArrayPool<byte>.Shared.Rent(serialized.Length + _command.Length + _end.Length + 4);
            
            var consumed = 4;
            _command.CopyTo(buffer.AsMemory(consumed));
            consumed += _command.Length;

            serialized.CopyTo(buffer.AsMemory(consumed));
            consumed += serialized.Length;

            _end.CopyTo(buffer.AsMemory(consumed));
            consumed += _end.Length;

            BitConverter.TryWriteBytes(buffer, consumed-4);
            return buffer;
        }
    }
}