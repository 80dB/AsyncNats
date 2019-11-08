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
            var rented = NatsConnect.RentedSerialize(_connect);
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            Assert.EndsWith("\r\n", text);
            ArrayPool<byte>.Shared.Return(rented);
        }

        [Fact]
        public void StartWithConnect()
        {
            var rented = NatsConnect.RentedSerialize(_connect);
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            Assert.StartsWith("CONNECT", text);
            ArrayPool<byte>.Shared.Return(rented);
        }

        [Fact]
        public void BeSame()
        {
            var rented = NatsConnect.RentedSerialize(_connect);
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);

            text = text.Replace("CONNECT ", "");
            text = text.Replace("\r\n", "");

            Assert.Equal(JsonSerializer.Serialize(_connect, new JsonSerializerOptions {IgnoreNullValues = true}), text);
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}