namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Linq;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsMsg_ParseMessageShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#msg

        [Fact]
        public void ReturnNatsMsg()
        {
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("Hello World\r\n")));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("MSG FOO.BAR 9 11"));
            var msg = new NatsMessageParser().ParseMessage( message, ref reader,out _);
            Assert.IsType<NatsMsg>(msg);
            ((NatsMsg) msg).Release();
        }

        [Fact]
        public void ReturnNull()
        {
            var reader = new SequenceReader<byte>();
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("MSG FOO.BAR 9 11"));
            var msg = new NatsMessageParser().ParseMessage(message, ref reader,out _);
            Assert.Null(msg);
        }

        [Fact]
        public void ReturnCorrectContentWithoutRelpyTo()
        {
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("Hello World\r\n")));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("MSG FOO.BAR 9 11"));
            var msg = (NatsMsg)new NatsMessageParser().ParseMessage(message, ref reader,out _);
            Assert.Equal("FOO.BAR", msg.Subject.AsString());
            Assert.Equal(9, msg.SubscriptionId);
            Assert.Equal(11, msg.Payload.Length);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(msg.Payload.Span));
            msg.Release();
        }

        [Fact]
        public void ReturnCorrectContentWithReplyTo()
        {
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("Hello World\r\n")));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("MSG FOO.BAR 9 INBOX.34 11"));
            var msg = (NatsMsg)new NatsMessageParser().ParseMessage(message, ref reader,out _);
            Assert.Equal("FOO.BAR", msg.Subject.AsString());
            Assert.Equal(9, msg.SubscriptionId);
            Assert.Equal("INBOX.34", msg.ReplyTo.AsString());
            Assert.Equal(11, msg.Payload.Length);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(msg.Payload.Span));
            msg.Release();
        }

        [Fact]
        public void ReturnCorrectContentWithReplyToAndWithHeaders()
        {
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("NATS/1.0\r\nkey:value\r\n\r\nHello World\r\n")));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("HMSG FOO.BAR 9 INBOX.34 23 34"));
            var msg = (NatsMsg)new NatsMessageParser().ParseMessageWithHeader( message, ref reader,out _);
            Assert.Equal("FOO.BAR", msg.Subject.AsString());
            Assert.Equal(9, msg.SubscriptionId);
            Assert.Equal("INBOX.34", msg.ReplyTo.AsString());
            Assert.Equal(11, msg.Payload.Length);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(msg.Payload.Span));
            Assert.True(msg.Headers.ReadAsString().Count() == 1);

            var firstHeader = msg.Headers.ReadAsString().First();
            Assert.Equal("key", firstHeader.Key);
            Assert.Equal("value", firstHeader.Value);
            msg.Release();
        }

        [Fact]
        public void ReturnCorrectContentWithoutReplyToAndWithHeaders()
        {
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("NATS/1.0\r\nkey:value\r\n\r\nHello World\r\n")));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("HMSG FOO.BAR 9 23 34"));
            var msg = (NatsMsg)new NatsMessageParser().ParseMessageWithHeader(message, ref reader,out _);
            Assert.Equal("FOO.BAR", msg.Subject.AsString());
            Assert.Equal(9, msg.SubscriptionId);
            Assert.True(msg.ReplyTo.IsEmpty);
            Assert.Equal(11, msg.Payload.Length);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(msg.Payload.Span));
            Assert.True(msg.Headers.ReadAsString().Count() == 1);

            var firstHeader = msg.Headers.ReadAsString().First();
            Assert.Equal("key", firstHeader.Key);
            Assert.Equal("value", firstHeader.Value);
            msg.Release();
        }
    }
}