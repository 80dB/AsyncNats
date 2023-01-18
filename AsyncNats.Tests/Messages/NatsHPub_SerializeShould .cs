namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsHPub_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#pub

        [Fact]
        public void BeSameWithoutReplyTo()
        {
            NatsMsgHeaders headers = new Dictionary<string, string>() { ["key"] = "value" };

            var rented = NatsHPub.Serialize( "FOO", NatsKey.Empty, headers, Encoding.UTF8.GetBytes("Hello NATS!"));
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("HPUB FOO 23 34\r\nNATS/1.0\r\nkey:value\r\n\r\nHello NATS!\r\n", text);
        }

        [Fact]
        public void BeSameWithReplyTo()
        {
            NatsMsgHeaders headers = new Dictionary<string, string>() { ["key"] = "value" };
            var rented = NatsHPub.Serialize("FRONT.DOOR", "INBOX.22", headers, Encoding.UTF8.GetBytes("Knock Knock"));
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("HPUB FRONT.DOOR INBOX.22 23 34\r\nNATS/1.0\r\nkey:value\r\n\r\nKnock Knock\r\n", text);
        }

        [Fact]
        public void ThrowOnInvalidHeader()
        {
            Assert.Throws<ArgumentException>("key" ,() => new NatsMsgHeaders( new Dictionary<string, string>() { ["ke:y"] = "value" }));
            Assert.Throws<ArgumentException>("value", () => new NatsMsgHeaders(new Dictionary<string, string>() { ["key"] = "v\ralue" }));

        }


    }
}