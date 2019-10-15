namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsUnsub_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#unsub

        [Fact]
        public void BeSameWithoutMaxMessages()
        {
            var rented = NatsUnsub.RentedSerialize("1", null);
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            ArrayPool<byte>.Shared.Return(rented);

            Assert.Equal("UNSUB 1\r\n", text);
        }

        [Fact]
        public void BeSameWitMaxMessages()
        {
            var rented = NatsUnsub.RentedSerialize("1", 5);
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            ArrayPool<byte>.Shared.Return(rented);

            Assert.Equal("UNSUB 1 5\r\n", text);
        }
    }
}