namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsPong_ParseMessageShould
    {
        private ReadOnlyMemory<byte> _message = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("PONG\r\n"));

        [Fact]
        public void ReturnNatsPong()
        {
            var reader = new SequenceReader<byte>();
            var pong = NatsPong.ParseMessage(new NatsMemoryPool(),  _message.Span, ref reader);
            Assert.IsType<NatsPong>(pong);
        }
    }
}