namespace AsyncNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsInformation_ParseMessageShould
    {
        // https://nats-io.github.io/docs/nats_protocol/nats-protocol.html#info
        private const string _serverId = "Zk0GQ3JBSrg3oyxCRRlE09";
        private const string _message = "INFO {\"server_id\":\"Zk0GQ3JBSrg3oyxCRRlE09\",\"version\":\"1.2.0\",\"proto\":1,\"go\":\"go1.10.3\",\"host\":\"0.0.0.0\",\"port\":4222,\"max_payload\":1048576,\"client_id\":2392}";
        private ReadOnlyMemory<byte> _memory = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(_message));

        [Fact]
        public void ReturnNatsInformation()
        {
            var reader = new SequenceReader<byte>();
            var info = NatsInformation.ParseMessage(new NatsMemoryPool(), _memory.Span, ref reader);
            Assert.IsType<NatsInformation>(info);
        }

        [Fact]
        public void ReturnSameServerId()
        {
            var reader = new SequenceReader<byte>();
            var info = (NatsInformation) NatsInformation.ParseMessage(new NatsMemoryPool(), _memory.Span, ref reader);
            Assert.Equal(_serverId, info.ServerId);
        }
    }
}