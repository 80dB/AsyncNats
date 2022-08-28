namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class NatsConnect
    {
        private static readonly ReadOnlyMemory<byte> _command = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("CONNECT "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        [JsonPropertyName("verbose")]
        public bool Verbose { get; set; }

        [JsonPropertyName("pedantic")]
        public bool Pedantic { get; set; }

        [JsonPropertyName("tls_required")]
        public bool TlsRequired { get; set; }

        [JsonPropertyName("auth_token")]
        public string? AuthorizationToken { get; set; }

        [JsonPropertyName("user")]
        public string? Username { get; set; }

        [JsonPropertyName("pass")]
        public string? Password { get; set; }


        [JsonPropertyName("name")]
        public string Name { get; set; } = "AsyncNats";

        [JsonPropertyName("lang")]
        public string Lang { get; set; } = "C#";

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("protocol")]
        public int Protocol { get; set; } = 1;

        [JsonPropertyName("echo")]
        public bool Echo { get; set; }

        [JsonPropertyName("headers")]
        public bool Headers { get; set; } = true;


       


        public NatsConnect()
        {
            Version = GetType().Assembly.GetName().Version.ToString();
        }

        public NatsConnect(INatsOptions options)
            : this()
        {
            Verbose = options.Verbose;

            AuthorizationToken = options.AuthorizationToken;
            Username = options.Username;
            Password = options.Password;

            Echo = options.Echo;

        }

        public static ReadOnlyMemory<byte> Serialize(NatsConnect msg)
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(msg, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            var hint = _command.Length +
                       serialized.Length +
                       _end.Length;

            var rented = new byte[hint];
            var buffer = rented.AsMemory();
            _command.CopyTo(buffer);
            var consumed = _command.Length;

            serialized.CopyTo(buffer.Slice(consumed));
            consumed += serialized.Length;

            _end.CopyTo(buffer.Slice(consumed));
            return rented;
        }

        
    }
}