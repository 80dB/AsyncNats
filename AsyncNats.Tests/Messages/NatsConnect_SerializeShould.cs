using System.Text.Json.Serialization;

namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using System.Text.Json;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsConnect_SerializeShould
    {
        private NatsConnect _connect;

        public NatsConnect_SerializeShould()
        {
            _connect = new NatsConnect();
        }

        [Fact]
        public void EndWithCRLF()
        {
            var rented = NatsConnect.Serialize(_connect);
            var text = Encoding.UTF8.GetString(rented.Span);
            Assert.EndsWith("\r\n", text);
        }

        [Fact]
        public void StartWithConnect()
        {
            var rented = NatsConnect.Serialize(_connect);
            var text = Encoding.UTF8.GetString(rented.Span);
            Assert.StartsWith("CONNECT", text);
        }

        [Fact]
        public void BeSame()
        {
            var rented = NatsConnect.Serialize(_connect);
            var text = Encoding.UTF8.GetString(rented.Span);

            text = text.Replace("CONNECT ", "");
            text = text.Replace("\r\n", "");

            Assert.Equal(JsonSerializer.Serialize(_connect, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }), text);
        }
    }
}