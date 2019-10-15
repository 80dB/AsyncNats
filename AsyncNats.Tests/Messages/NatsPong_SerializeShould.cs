namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsPong_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#pong

        [Fact]
        public void BeSame()
        {
            var rented = NatsPong.RentedSerialize();
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            ArrayPool<byte>.Shared.Return(rented);

            Assert.Equal("PONG\r\n", text);
        }
    }
}