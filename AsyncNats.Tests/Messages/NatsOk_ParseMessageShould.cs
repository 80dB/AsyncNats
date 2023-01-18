namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsOk_ParseMessageShould
    {
        private ReadOnlyMemory<byte> _message = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("+OK\r\n"));

        [Fact]
        public void ReturnNatsOk()
        {
            var ok = new NatsMessageParser().ParseOk();
            Assert.IsType<NatsOk>(ok);
        }
    }
}