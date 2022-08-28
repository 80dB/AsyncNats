namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsSub_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#sub

        [Fact]
        public void BeSameWithoutQueueGroup()
        {
            var rented = NatsSub.Serialize("FOO", NatsKey.Empty, 1);
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("SUB FOO 1\r\n", text);
        }

        [Fact]
        public void BeSameWithQueueGroup()
        {
            var rented = NatsSub.Serialize("BAR", "G1", 44);
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("SUB BAR G1 44\r\n", text);
        }
    }
}