namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Runtime.CompilerServices;
    using System.Threading;


    public readonly ref struct NatsInlineMsg
    {

        public readonly NatsInlineKey Subject;
        public readonly NatsInlineKey ReplyTo;
        public readonly long SubscriptionId;
        public readonly ReadOnlySequence<byte> Payload;
        public readonly NatsMsgHeadersRead Headers;

        public NatsInlineMsg(ref NatsInlineKey subject, ref NatsInlineKey replyTo,long subscriptionId, ReadOnlySequence<byte> payload, NatsMsgHeadersRead headers)
        {
            Subject = subject;
            ReplyTo = replyTo;
            SubscriptionId = subscriptionId;
            Payload = payload;
            Headers = headers; 
        }

    }

    public class NatsMsg : INatsServerMessage
    {
        private static readonly byte[] _empty = new byte[0];
        private int _referenceCounter;
        private NatsMemoryOwner? _rentedPayload;


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

        public NatsMsg(in NatsKey subject, in long subscriptionId, in NatsKey replyTo, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> headers,in NatsMemoryOwner rentedPayload)
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
                _rentedPayload?.Return();
                _rentedPayload = null;
            }
        }

        

    }
}