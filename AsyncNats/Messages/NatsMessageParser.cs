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

        private delegate INatsServerMessage? ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader);

        //leave this list in order of usage frequency so we avoid loop runs when parsing
        private static readonly Tuple<byte[], ParseMessage>[] _messageParsers =
        {
            new Tuple<byte[], ParseMessage>(_ok, NatsOk.ParseMessage),
            new Tuple<byte[], ParseMessage>(_message, NatsMsg.ParseMessage),
            new Tuple<byte[], ParseMessage>(_messageWithHeader, NatsMsg.ParseMessageWithHeader),
            new Tuple<byte[], ParseMessage>(_ping, NatsPing.ParseMessage),
            new Tuple<byte[], ParseMessage>(_pong, NatsPong.ParseMessage),
            new Tuple<byte[], ParseMessage>(_error, NatsError.ParseMessage),
            new Tuple<byte[], ParseMessage>(_information, NatsInformation.ParseMessage)
        };

        
        public int ParseMessages(in ReadOnlySequence<byte> buffer, Span<INatsServerMessage> outMessages, out long consumed)
        {
            var messageCount = 0;
            var reader = new SequenceReader<byte>(buffer);
            while (messageCount<outMessages.Length)
            {
                var previousPosition = reader.Consumed;
                if (!reader.TryReadTo(out ReadOnlySpan<byte> line, (byte) '\n')) break;
                line = line.Slice(0, line.Length - 1); // Slice out \r as well (not just \n)

                ParseMessage? parseMessage = null;
                // Not a foreach as it generates an enumerator which allocates bytes
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < _messageParsers.Length; i++)
                {
                    if (line.StartsWith(_messageParsers[i].Item1))
                    {
                        parseMessage = _messageParsers[i].Item2;
                        break;
                    }
                }
                
                if (parseMessage == null) throw new ProtocolViolationException($"Unknown message {Encoding.UTF8.GetString(line)}");
                var message = parseMessage(_memoryPool, line, ref reader);
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