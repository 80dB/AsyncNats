namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class NatsInformation : INatsServerMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("INFO "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        [JsonPropertyName("server_id")]
        public string ServerId { get; set; } = string.Empty;
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
        [JsonPropertyName("proto")]
        public int Protocol { get; set; }
        [JsonPropertyName("go")]
        public string GoVersion { get; set; } = string.Empty;
        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;
        [JsonPropertyName("port")]
        public int Port { get; set; }
        [JsonPropertyName("max_payload")]
        public int MaxPayload { get; set; }
        [JsonPropertyName("client_id")]
        public int ClientId { get; set; }

        public static INatsServerMessage? ParseMessage(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            // Remove "INFO " and parse remainder as JSON
            return JsonSerializer.Deserialize<NatsInformation>(line.Slice(5));
        }
    }
}