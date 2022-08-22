namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Net;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Xml.Linq;

    public class NatsMsg : INatsServerMessage
    {
        private static readonly byte[] _empty = new byte[0];
        private int _referenceCounter;
        private IMemoryOwner<byte>? _rentedPayload;


        public readonly NatsKey Subject;
        public readonly NatsKey ReplyTo;
        public readonly long SubscriptionId;

        public ReadOnlyMemory<byte> Payload { get; private set; }

        private readonly ReadOnlyMemory<byte> _headerMemory;
        private NatsMsgHeadersRead? _headers;

        public NatsMsgHeadersRead Headers
        {
            get
            {
                if (_headers != null)
                    return _headers.Value;

                if (_headerMemory.IsEmpty == false)
                    _headers = new NatsMsgHeadersRead(_headerMemory);
                else
                    _headers = NatsMsgHeadersRead.Empty;

                return _headers.Value;
            }
        }
        public NatsMsg(in NatsKey subject, in long subscriptionId, in NatsKey replyTo, ReadOnlyMemory<byte> payload)
        {
            Subject = subject;
            SubscriptionId = subscriptionId;
            ReplyTo = replyTo;
            Payload = payload;
            _headerMemory = ReadOnlyMemory<byte>.Empty;
            _rentedPayload = null;
            _referenceCounter = -1;
        }

        public NatsMsg(in NatsKey subject, in long subscriptionId, in NatsKey replyTo, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> headers, IMemoryOwner<byte> rentedPayload)
        {
            Subject = subject;
            SubscriptionId = subscriptionId;
            ReplyTo = replyTo;
            Payload = payload;
            _headerMemory = headers;
            _rentedPayload = rentedPayload;
            _referenceCounter = 1;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rent()
        {
            Interlocked.Increment(ref _referenceCounter);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Release()
        {
            if (Interlocked.Decrement(ref _referenceCounter) == 0)
            {
                _rentedPayload?.Dispose();
                _rentedPayload = null;
            }
        }

        public static INatsServerMessage? ParseMessage(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {

            //the idea here is to walk the *line* backwards in sequence just once
            //this should prevent memory fetching as it will usually fit into one cache line
            //it also helps that parsing int is done from right to left
            //we trust nats won't send malformed header lines

            //parse total size
            var multiplier = 1;
            var payloadSize = 0;
            var pointer = line.Length - 1;

            do
            {
                payloadSize += (line[pointer] - '0') * multiplier;
                multiplier *= 10;
                pointer--;
            } while (line[pointer] != ' ');

            var payloadSizeStart = pointer;
            pointer--;

            Span<int> splits = stackalloc int[3];
            var splitCount = 0;

            while (pointer > 3)
            {
                var ch = line[pointer];
                if (ch == ' ')
                {
                    splits[splitCount] = pointer;
                    splitCount++;

                    if (splitCount > 2)
                        throw new ProtocolViolationException($"Invalid message header {Encoding.UTF8.GetString(line)}");
                }

                pointer--;
            }
            //done with first line

            if (reader.Remaining < payloadSize + 2) return null;

            var wholeMessageSize = payloadSize + line.Length;
            var copyRented = pool.Rent(wholeMessageSize);

            //copy message first line
            var copyMemory = copyRented.Memory;
            line.CopyTo(copyMemory.Span);

            //copy payload + header
            copyMemory = copyRented.Memory.Slice(line.Length);
            reader.Sequence.Slice(reader.Position, payloadSize).CopyTo(copyMemory.Span);
            reader.Advance(payloadSize + 2);

            var payload = payloadSize > 0 ? copyMemory.Slice(0, payloadSize) : ReadOnlyMemory<byte>.Empty;

            //get pointers to strings
            NatsKey subject;
            NatsKey replyTo = NatsKey.Empty;
            long sid = 0;            

            copyMemory = copyRented.Memory;
            if (splitCount == 1)
            {
                //sid
                pointer = payloadSizeStart - 1;
                multiplier = 1;
                do
                {
                    sid += (line[pointer] - '0') * multiplier;
                    multiplier *= 10;
                    pointer--;
                } while (line[pointer] != ' ');

                subject = new NatsKey(copyMemory.Slice(4, splits[0] - 4));
            }
            else
            {
                replyTo = new NatsKey(copyMemory.Slice(splits[0] + 1, payloadSizeStart - splits[0] - 1));

                //sid
                pointer = splits[0] - 1;
                multiplier = 1;
                do
                {
                    sid += (line[pointer] - '0') * multiplier;
                    multiplier *= 10;
                    pointer--;
                } while (line[pointer] != ' ');

                subject = new NatsKey(copyMemory.Slice(4, splits[1] - 4));
            }

            return new NatsMsg(subject, sid, replyTo, payload, ReadOnlyMemory<byte>.Empty, copyRented);

        }



        public static INatsServerMessage? ParseMessageWithHeader(NatsMemoryPool pool, in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {

            //parse total size
            var multiplier = 1;
            var totalSize = 0;
            var totalSizeStart = line.Length - 1;
            do
            {
                totalSize += (line[totalSizeStart] - '0') * multiplier;
                multiplier *= 10;
                totalSizeStart--;
            } while (line[totalSizeStart] != ' ');

            //parse header size
            multiplier = 1;
            var headerSize = 0;
            var headerSizeStart = totalSizeStart - 1;
            do
            {
                headerSize += (line[headerSizeStart] - '0') * multiplier;
                multiplier *= 10;
                headerSizeStart--;
            } while (line[headerSizeStart] != ' ');


            var pointer = headerSizeStart - 1;


            Span<int> splits = stackalloc int[3];
            var splitCount = 0;

            while (pointer > 4)
            {
                var ch = line[pointer];
                if (ch == ' ')
                {
                    splits[splitCount] = pointer;
                    splitCount++;

                    if (splitCount > 2)
                        throw new ProtocolViolationException($"Invalid message header {Encoding.UTF8.GetString(line)}");
                }

                pointer--;
            }
            var payloadSize = totalSize - headerSize;

           

            //done with first line

            if (reader.Remaining < totalSize + 2) return null;

            var wholeMessageSize = totalSize + line.Length;
            var copyRented = pool.Rent(wholeMessageSize);

            //copy message first line
            var copyMemory = copyRented.Memory;
            line.CopyTo(copyMemory.Span);

            //copy payload + header
            copyMemory = copyRented.Memory.Slice(line.Length);
            reader.Sequence.Slice(reader.Position, totalSize).CopyTo(copyMemory.Span);
            reader.Advance(totalSize + 2);

            var headers = copyMemory.Slice(0, headerSize);
            var payload = payloadSize > 0 ? copyMemory.Slice(headerSize, payloadSize) : ReadOnlyMemory<byte>.Empty;

            //get pointers to strings
            NatsKey subject;            
            NatsKey replyTo = NatsKey.Empty;
            long sid = 0;

            copyMemory = copyRented.Memory;
            if (splitCount == 1)
            {
                //sid
                pointer = headerSizeStart - 1;
                multiplier = 1;
                do
                {
                    sid += (line[pointer] - '0') * multiplier;
                    multiplier *= 10;
                    pointer--;
                } while (line[pointer] != ' ');

                subject = new NatsKey(copyMemory.Slice(5, splits[0] - 5));
            }
            else
            {
                replyTo = new NatsKey(copyMemory.Slice(splits[0] + 1, headerSizeStart - splits[0] - 1));

                //sid
                pointer = splits[0] - 1;
                multiplier = 1;
                do
                {
                    sid += (line[pointer] - '0') * multiplier;
                    multiplier *= 10;
                    pointer--;
                } while (line[pointer] != ' ');

                subject = new NatsKey(copyMemory.Slice(5, splits[1] - 5));
            }

            return new NatsMsg(subject, sid, replyTo, payload, headers, copyRented);



        }

    }
}