namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsError_ParseMessageShould
    {
        private ReadOnlyMemory<byte> _withErrorMessage = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("-ERR 'Stale Connection'\r\n"));
        private ReadOnlyMemory<byte> _withoutErrorMessage = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("-ERR\r\n"));

        [Fact]
        public void ReturnNatsError()
        {
            var reader = new SequenceReader<byte>();
            var err = NatsError.ParseMessage(new NatsMemoryPool(), _withErrorMessage.Span, ref reader);
            Assert.IsType<NatsError>(err);
        }

        [Fact]
        public void WorkWithMessage()
        {
            var reader = new SequenceReader<byte>();
            var err = (NatsError) NatsError.ParseMessage(new NatsMemoryPool(), _withErrorMessage.Span, ref reader);
            Assert.Equal("Stale Connection", err.Error);
        }

        [Fact]
        public void WorkWithoutMessage()
        {
            var reader = new SequenceReader<byte>();
            var err = (NatsError) NatsError.ParseMessage(new NatsMemoryPool(), _withoutErrorMessage.Span, ref reader);
            Assert.Null(err.Error);
        }
    }
}