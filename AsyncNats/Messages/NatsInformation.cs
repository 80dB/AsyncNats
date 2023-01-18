namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class NatsInformation : INatsServerMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("INFO "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        [JsonInclude]
        [JsonPropertyName("server_id")]        
        public string ServerId { get; private set; } = string.Empty;

        [JsonInclude]
        [JsonPropertyName("version")]
        public string Version { get; private  set; } = string.Empty;

        [JsonInclude]
        [JsonPropertyName("proto")]
        public int Protocol { get; private set; }

        [JsonInclude]
        [JsonPropertyName("headers")]
        public bool HeadersSupported { get; private set; }

        [JsonInclude]
        [JsonPropertyName("go")]
        public string GoVersion { get; private set; } = string.Empty;

        [JsonInclude]
        [JsonPropertyName("host")]
        public string Host { get; private set; } = string.Empty;

        [JsonInclude]
        [JsonPropertyName("port")]
        public int Port { get; private set; }

        [JsonInclude]
        [JsonPropertyName("max_payload")]
        public int MaxPayload { get; private set; }

        [JsonInclude]
        [JsonPropertyName("client_id")]
        public int ClientId { get; private set; }

        [JsonInclude]
        [JsonPropertyName("connect_urls")]
        public string[] ConnectURLs { get; private set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}