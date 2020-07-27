namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsOk_ParseMessageShould
    {
        private ReadOnlyMemory<byte> _message = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("+OK\r\n"));

        [Fact]
        public void ReturnNatsOk()
        {
            var reader = new SequenceReader<byte>();
            var ok = NatsOk.ParseMessage(new NatsMemoryPool(), _message.Span, ref reader);
            Assert.IsType<NatsOk>(ok);
        }
    }
}