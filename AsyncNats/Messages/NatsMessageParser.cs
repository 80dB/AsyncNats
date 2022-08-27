namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;

    public class NatsMessageParser
    {
        private readonly NatsMemoryPool _memoryPool;

        public NatsMessageParser(NatsMemoryPool memoryPool)
        {
            _memoryPool = memoryPool;
        }

        private static readonly byte[] _information = Encoding.UTF8.GetBytes("INFO ");
        private static readonly byte[] _message = Encoding.UTF8.GetBytes("MSG ");
        private static readonly byte[] _messageWithHeader = Encoding.UTF8.GetBytes("HMSG ");
        private static readonly byte[] _ok = Encoding.UTF8.GetBytes("+OK");
        private static readonly byte[] _error = Encoding.UTF8.GetBytes("-ERR");
        private static readonly byte[] _ping = Encoding.UTF8.GetBytes("PING");
        private static readonly byte[] _pong = Encoding.UTF8.GetBytes("PONG");

        public int ParseMessages(in ReadOnlySequence<byte> buffer, Span<INatsServerMessage> outMessages, out long consumed)
        {
            var messageCount = 0;
            var reader = new SequenceReader<byte>(buffer);
            while (messageCount<outMessages.Length)
            {
                var previousPosition = reader.Consumed;
                if (!reader.TryReadTo(out ReadOnlySpan<byte> line, (byte) '\n')) break;
                line = line.Slice(0, line.Length - 1); // Slice out \r as well (not just \n)

                INatsServerMessage? message = null;
                switch (line[0])
                {                  
                    case (byte)'+': message = NatsOk.ParseMessage(_memoryPool, line, ref reader); break;
                    case (byte)'M': message = NatsMsg.ParseMessage(_memoryPool, line, ref reader); break;
                    case (byte)'H': message = NatsMsg.ParseMessageWithHeader(_memoryPool, line, ref reader); break;
                    case (byte)'I': message = NatsInformation.ParseMessage(_memoryPool, line, ref reader); break;
                    case (byte)'-': message = NatsError.ParseMessage(_memoryPool, line, ref reader); break;
                    case (byte)'P': message = line[1] == (byte)'I' ? NatsPing.ParseMessage(_memoryPool, line, ref reader) :
                            NatsPong.ParseMessage(_memoryPool, line, ref reader); break;
                    default:
                        throw new ProtocolViolationException($"Unknown message {Encoding.UTF8.GetString(line)}");
                }
        
                if (message == null)
                {
                    // Not enough information to parse the message
                    reader.Rewind(reader.Consumed - previousPosition);
                    break;
                }

                outMessages[messageCount] = message;
                messageCount++;
               
            }

            consumed = reader.Consumed;
            return messageCount;
        }
    }
}