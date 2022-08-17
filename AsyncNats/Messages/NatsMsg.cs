﻿namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Net;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;

    public class NatsMsg : INatsServerMessage
    {
        private static readonly byte[] _empty = new byte[0];
        private int _referenceCounter;
        private IMemoryOwner<byte>? _rentedPayload;

        public readonly Utf8String Subject;
        public readonly Utf8String SubscriptionId;
        public readonly Utf8String ReplyTo;
        public ReadOnlyMemory<byte> Payload { get; private set; }

        public NatsMsg(in Utf8String subject, in Utf8String subscriptionId, in Utf8String replyTo, ReadOnlyMemory<byte> payload)
        {
            Subject = subject;
            SubscriptionId = subscriptionId;
            ReplyTo = replyTo;
            Payload = payload;
            _rentedPayload = null;
            _referenceCounter = -1;
        }

        public NatsMsg(in Utf8String subject,in Utf8String subscriptionId, in Utf8String replyTo, ReadOnlyMemory<byte> payload, IMemoryOwner<byte> rentedPayload)
        {
            Subject = subject;
            SubscriptionId = subscriptionId;
            ReplyTo = replyTo;
            Payload = payload;
            _rentedPayload = rentedPayload;
            _referenceCounter = 1;
        }

        public void Rent()
        {            
            Interlocked.Increment(ref _referenceCounter);
        }

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
            //parse payload size
            var multiplier = 1;
            var payloadSize = 0;
            var payloadSizeStart = line.Length - 1;
            do
            {
                payloadSize += (line[payloadSizeStart] - '0') * multiplier;
                multiplier *= 10;
                payloadSizeStart--;
            } while (line[payloadSizeStart] != ' ');


            if (reader.Remaining < payloadSize + 2) return null;

            var wholeMessageSize = payloadSize + line.Length;
            var copyRented = pool.Rent(wholeMessageSize);

            //copy message header
            var copyMemory = copyRented.Memory;
            line.CopyTo(copyMemory.Span);

            //copy payload
            copyMemory = copyRented.Memory.Slice(line.Length);
            reader.Sequence.Slice(reader.Position, payloadSize).CopyTo(copyMemory.Span);
            reader.Advance(payloadSize + 2);
            var payload = copyMemory.Slice(0, payloadSize);

            //get pointers from header
            var next = copyRented.Memory.Slice(4);
            var part = next.Slice(0, next.Span.IndexOf((byte)' '));
            var subject = new Utf8String(part, convert: false);

            next = next.Slice(part.Length + 1);
            part = next.Slice(0, next.Span.IndexOf((byte)' '));
            var sid = new Utf8String(part, convert: false);

            next = next.Slice(part.Length + 1);
            var replyTo = Utf8String.Empty;
            var split = next.Span.IndexOf((byte)' ');
            if (split > 0)
            {
                replyTo = new Utf8String(next.Slice(0, split), convert: false);
            }

            return new NatsMsg(subject, sid, replyTo, payload, copyRented);
        }
    }
}