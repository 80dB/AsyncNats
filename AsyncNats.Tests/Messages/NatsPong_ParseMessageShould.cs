namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsPong_ParseMessageShould
    {
        private ReadOnlyMemory<byte> _message = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("PONG\r\n"));

        [Fact]
        public void ReturnNatsPong()
        {
            var pong = new NatsMessageParser().ParsePong();
            Assert.IsType<NatsPong>(pong);
        }
    }
}