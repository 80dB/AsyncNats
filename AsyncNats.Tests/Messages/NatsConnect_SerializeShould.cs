namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using System.Text.Json;
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
            using var rented = NatsConnect.RentedSerialize(new NatsMemoryPool(), _connect);
            var text = Encoding.UTF8.GetString(rented.Memory.Span);
            Assert.EndsWith("\r\n", text);
        }

        [Fact]
        public void StartWithConnect()
        {
            using var rented = NatsConnect.RentedSerialize(new NatsMemoryPool(), _connect);
            var text = Encoding.UTF8.GetString(rented.Memory.Span);
            Assert.StartsWith("CONNECT", text);
        }

        [Fact]
        public void BeSame()
        {
            using var rented = NatsConnect.RentedSerialize(new NatsMemoryPool(), _connect);
            var text = Encoding.UTF8.GetString(rented.Memory.Span);

            text = text.Replace("CONNECT ", "");
            text = text.Replace("\r\n", "");

            Assert.Equal(JsonSerializer.Serialize(_connect, new JsonSerializerOptions {IgnoreNullValues = true}), text);
        }
    }
}