namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;

    public class NatsMessageParser
    {
        private static readonly byte[] _information = Encoding.UTF8.GetBytes("INFO ");
        private static readonly byte[] _message = Encoding.UTF8.GetBytes("MSG ");
        private static readonly byte[] _ok = Encoding.UTF8.GetBytes("+OK");
        private static readonly byte[] _error = Encoding.UTF8.GetBytes("-ERR");
        private static readonly byte[] _ping = Encoding.UTF8.GetBytes("PING");
        private static readonly byte[] _pong = Encoding.UTF8.GetBytes("PONG");

        private delegate INatsServerMessage? ParseMessage(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader);

        private static readonly Tuple<byte[], ParseMessage>[] _messageParsers =
        {
            new Tuple<byte[], ParseMessage>(_message, NatsMsg.ParseMessage),
            new Tuple<byte[], ParseMessage>(_ping, NatsPing.ParseMessage),
            new Tuple<byte[], ParseMessage>(_pong, NatsPong.ParseMessage),
            new Tuple<byte[], ParseMessage>(_ok, NatsOk.ParseMessage),
            new Tuple<byte[], ParseMessage>(_error, NatsError.ParseMessage),
            new Tuple<byte[], ParseMessage>(_information, NatsInformation.ParseMessage)
        };

        public List<INatsServerMessage> ParseMessages(in ReadOnlySequence<byte> buffer, out long consumed)
        {
            var messages = new List<INatsServerMessage>();
            var reader = new SequenceReader<byte>(buffer);
            while (true)
            {
                var previousPosition = reader.Consumed;
                if (!reader.TryReadTo(out ReadOnlySpan<byte> line, (byte) '\n')) break;
                line = line.Slice(0, line.Length - 1); // Slice out \r as well (not just \n)

                ParseMessage? parseMessage = null;
                // Not a foreach as it generates an enumerator which allocates bytes
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < _messageParsers.Length; i++)
                {
                    if (!line.StartsWith(_messageParsers[i].Item1)) continue;
                    parseMessage = _messageParsers[i].Item2;
                    break;
                }

                if (parseMessage == null) throw new ProtocolViolationException($"Unknown message {Encoding.UTF8.GetString(line)}");
                var message = parseMessage(line, ref reader);
                if (message == null)
                {
                    // Not enough information to parse the message
                    reader.Rewind(reader.Consumed - previousPosition);
                    break;
                }

                messages.Add(message);
            }

            consumed = reader.Consumed;
            return messages;
        }
    }
}