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
            using var rented = NatsUnsub.RentedSerialize(new NatsMemoryPool(), "1", null);
            var text = Encoding.UTF8.GetString(rented.Memory.Span);

            Assert.Equal("UNSUB 1\r\n", text);
        }

        [Fact]
        public void BeSameWitMaxMessages()
        {
            using var rented = NatsUnsub.RentedSerialize(new NatsMemoryPool(), "1", 5);
            var text = Encoding.UTF8.GetString(rented.Memory.Span);

            Assert.Equal("UNSUB 1 5\r\n", text);
        }
    }
}