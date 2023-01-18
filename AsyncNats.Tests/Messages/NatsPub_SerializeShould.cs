namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsPub_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#pub

        [Fact]
        public void BeSameWithoutReplyTo()
        {
            var rented = NatsPub.Serialize( "FOO", NatsKey.Empty, Encoding.UTF8.GetBytes("Hello NATS!"));
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("PUB FOO 11\r\nHello NATS!\r\n", text);
        }

        [Fact]
        public void BeSameWithReplyTo()
        {
            var rented = NatsPub.Serialize("FRONT.DOOR", "INBOX.22", Encoding.UTF8.GetBytes("Knock Knock"));
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("PUB FRONT.DOOR INBOX.22 11\r\nKnock Knock\r\n", text);
        }

        [Fact]
        public void BeSameWithReplyToLength999()
        {
            var payload = new byte[999];
            for (var i = 0; i < 999; i++) payload[i] = (byte)'*';
            var rented = NatsPub.Serialize( "FRONT.DOOR", "INBOX.22", payload);
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("PUB FRONT.DOOR INBOX.22 999\r\n" + Encoding.UTF8.GetString(payload) + "\r\n", text);
        }
    }
}