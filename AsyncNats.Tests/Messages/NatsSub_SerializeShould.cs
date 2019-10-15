namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsSub_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#sub

        [Fact]
        public void BeSameWithoutQueueGroup()
        {
            var rented = NatsSub.RentedSerialize("FOO", null, "1");
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            ArrayPool<byte>.Shared.Return(rented);

            Assert.Equal("SUB FOO 1\r\n", text);
        }

        [Fact]
        public void BeSameWithQueueGroup()
        {
            var rented = NatsSub.RentedSerialize("BAR", "G1", "44");
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            ArrayPool<byte>.Shared.Return(rented);

            Assert.Equal("SUB BAR G1 44\r\n", text);
        }
    }
}