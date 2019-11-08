namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsMsg_ParseMessageShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#msg

        [Fact]
        public void ReturnNatsMsg()
        {
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("Hello World\r\n")));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("MSG FOO.BAR 9 11\r\n"));
            var msg = NatsMsg.ParseMessage(message, ref reader);
            Assert.IsType<NatsMsg>(msg);
            ((NatsMsg) msg).Release();
        }

        [Fact]
        public void ReturnNull()
        {
            var reader = new SequenceReader<byte>();
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("MSG FOO.BAR 9 11\r\n"));
            var msg = NatsMsg.ParseMessage(message, ref reader);
            Assert.Null(msg);
        }

        [Fact]
        public void ReturnCorrectContentWithoutRelpyTo()
        {
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("Hello World\r\n")));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("MSG FOO.BAR 9 11\r\n"));
            var msg = (NatsMsg) NatsMsg.ParseMessage(message, ref reader);
            Assert.Equal("FOO.BAR", msg.Subject);
            Assert.Equal("9", msg.SubscriptionId);
            Assert.Equal(11, msg.Payload.Length);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(msg.Payload.Span));
            msg.Release();
        }

        [Fact]
        public void ReturnCorrectContentWithReplyTo()
        {
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("Hello World\r\n")));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("MSG FOO.BAR 9 INBOX.34 11\r\n"));
            var msg = (NatsMsg) NatsMsg.ParseMessage(message, ref reader);
            Assert.Equal("FOO.BAR", msg.Subject);
            Assert.Equal("9", msg.SubscriptionId);
            Assert.Equal("INBOX.34", msg.ReplyTo);
            Assert.Equal(11, msg.Payload.Length);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(msg.Payload.Span));
            msg.Release();
        }
    }
}