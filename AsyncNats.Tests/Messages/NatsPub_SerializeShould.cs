namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsPub_SerializeShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#pub

        [Fact]
        public void BeSameWithoutReplyTo()
        {
            var rented = NatsPub.RentedSerialize("FOO", null, Encoding.UTF8.GetBytes("Hello NATS!"));
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            ArrayPool<byte>.Shared.Return(rented);

            Assert.Equal("PUB FOO 11\r\nHello NATS!\r\n", text);
        }

        [Fact]
        public void BeSameWithReplyTo()
        {
            var rented = NatsPub.RentedSerialize("FRONT.DOOR", "INBOX.22", Encoding.UTF8.GetBytes("Knock Knock"));
            var consumed = BitConverter.ToInt32(rented);
            var text = Encoding.UTF8.GetString(rented, 4, consumed);
            ArrayPool<byte>.Shared.Return(rented);

            Assert.Equal("PUB FRONT.DOOR INBOX.22 11\r\nKnock Knock\r\n", text);
        }
    }
}