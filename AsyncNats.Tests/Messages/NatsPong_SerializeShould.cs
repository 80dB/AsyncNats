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
            using var rented = NatsPong.RentedSerialize(new NatsMemoryPool());
            var text = Encoding.UTF8.GetString(rented.Memory.Span);

            Assert.Equal("PONG\r\n", text);
        }
    }
}