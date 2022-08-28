namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsPong_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#pong

        [Fact]
        public void BeSame()
        {
            var rented = NatsPong.Serialize();
            var text = Encoding.UTF8.GetString(rented.Span);

            Assert.Equal("PONG\r\n", text);
        }
    }
}