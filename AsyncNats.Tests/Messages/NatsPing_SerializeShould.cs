namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsPing_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#ping

        [Fact]
        public void BeSame()
        {
            using var rented = NatsPing.RentedSerialize(new NatsMemoryPool());
            var text = Encoding.UTF8.GetString(rented.Memory.Span);

            Assert.Equal("PING\r\n", text);
        }
    }
}