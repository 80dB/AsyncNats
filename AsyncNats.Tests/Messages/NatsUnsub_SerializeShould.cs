namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsUnsub_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#unsub

        [Fact]
        public void BeSameWithoutMaxMessages()
        {
            var rented = NatsUnsub.Serialize(1, null);
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("UNSUB 1\r\n", text);
        }

        [Fact]
        public void BeSameWitMaxMessages()
        {
            var rented = NatsUnsub.Serialize( 1, 5);
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("UNSUB 1 5\r\n", text);
        }

        [Fact]
        public void BeSameWitMaxMessages999()
        {
            var rented = NatsUnsub.Serialize(1, 999);
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("UNSUB 1 999\r\n", text);
        }
    }
}