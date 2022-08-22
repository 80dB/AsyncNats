namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;



    public readonly struct NatsMsgHeaders
    {
        public static readonly NatsMsgHeaders Empty = new NatsMsgHeaders(Enumerable.Empty<KeyValuePair<string, string>>());

        private static readonly ReadOnlyMemory<byte> _protocolVersion = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("NATS/1.0\r\n"));
        private static readonly ReadOnlyMemory<byte> _separator = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(":"));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        public readonly int SerializedLength;

        private readonly IEnumerable<KeyValuePair<string, string>> _headers;

        public NatsMsgHeaders(IEnumerable<KeyValuePair<string, string>> headers)
        {
            _headers = headers;
            SerializedLength = _protocolVersion.Length;
            foreach (var (k, v) in headers)
            {
                SerializedLength += k.Length + v.Length + _separator.Length + _end.Length;
            }
            SerializedLength+= _end.Length;

            foreach(var (k,v) in headers)
            {
                CheckKeyValue(k, v);
            }
        }

        private void CheckKeyValue(string key, string value)
        {
            //taken from https://github.com/nats-io/nats.net/blob/master/src/NATS.Client/MsgHeader.cs

            // values are OK to be null, check keys elsewhere.
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("key cannot be empty or null.","key");
            }

            foreach (char c in key)
            {
                // only printable characters and no colon
                if (c < 32 || c > 126 || c == ':')
                    throw new ArgumentException(string.Format("Invalid character {0:X2} in key.", c),"key");
            }

            if (value != null)
            {
                foreach (char c in value)
                {
                    if ((c < 32 && c != 9) || c > 126)
                        throw new ArgumentException(string.Format("Invalid character {0:X2} in value.", c),"value");
                }
            }
        }

        internal void SerializeTo(Span<byte> buffer)
        {

            _protocolVersion.Span.CopyTo(buffer);
            buffer = buffer.Slice(_protocolVersion.Length);

            foreach (var (k, v) in _headers!)
            {
                Encoding.UTF8.GetBytes(k, buffer);
                buffer = buffer.Slice(k.Length);

                _separator.Span.CopyTo(buffer);
                buffer = buffer.Slice(_separator.Length);

                Encoding.UTF8.GetBytes(v, buffer);
                buffer = buffer.Slice(v.Length);

                _end.Span.CopyTo(buffer);
                buffer = buffer.Slice(_end.Length);
            }

            _end.Span.CopyTo(buffer);            
        }

        //implicit conversions        
        public static implicit operator NatsMsgHeaders(Dictionary<string,string> headers) => new NatsMsgHeaders(headers);
        public static implicit operator NatsMsgHeaders(List<KeyValuePair<string, string>> headers) => new NatsMsgHeaders(headers);
        public static implicit operator NatsMsgHeaders(KeyValuePair<string, string>[] headers) => new NatsMsgHeaders(headers);

    }

}