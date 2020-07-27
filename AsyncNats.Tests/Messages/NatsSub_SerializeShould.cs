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
            using var rented = NatsSub.RentedSerialize(new NatsMemoryPool(), "FOO", null, "1");
            var text = Encoding.UTF8.GetString(rented.Memory.Span);

            Assert.Equal("SUB FOO 1\r\n", text);
        }

        [Fact]
        public void BeSameWithQueueGroup()
        {
            using var rented = NatsSub.RentedSerialize(new NatsMemoryPool(), "BAR", "G1", "44");
            var text = Encoding.UTF8.GetString(rented.Memory.Span);

            Assert.Equal("SUB BAR G1 44\r\n", text);
        }
    }
}