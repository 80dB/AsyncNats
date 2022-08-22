namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public readonly struct NatsMsgHeadersRead
    {
        public static readonly NatsMsgHeadersRead Empty = new NatsMsgHeadersRead();

        private static readonly ReadOnlyMemory<byte> _protocolVersion = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("NATS/1.0\r\n"));

        private readonly IEnumerable<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> _headers;

        public NatsMsgHeadersRead(ReadOnlyMemory<byte> data)
        {
            if (data.IsEmpty)
                _headers = Enumerable.Empty<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>>();
            else
                _headers = ParseHeaders(data);
        }
        
        
        public IEnumerable<KeyValuePair<string, string>> ReadAsString()
        {           
            if (_headers.Count()>0)
            {
                return _headers
                    .Select(x => new KeyValuePair<string, string>(Encoding.UTF8.GetString(x.Key.Span), Encoding.UTF8.GetString(x.Value.Span)));
            }
            else
                return Enumerable.Empty<KeyValuePair<string, string>>();
            
        }
        public IEnumerable<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> ReadAsBytes()
        {
            return _headers;
        }
        private static IEnumerable<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> ParseHeaders(ReadOnlyMemory<byte> data)
        {
            List<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> headers = new List<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>>(4);

            data = data.Slice(_protocolVersion.Length);

            while (data.Length > 0)
            {
                if (data.Span[0] == (byte)'\r')
                    break;

                var keyEnd = data.Span.IndexOf((byte)':');
                var key = data.Slice(0, keyEnd);
                data = data.Slice(keyEnd + 1);

                var valueEnd = data.Span.IndexOf((byte)'\r');
                var value = data.Slice(0, valueEnd);
                data = data.Slice(valueEnd + 2); //skip /r/n

                headers.Add(new KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>(key, value));
            }

            return headers;

        }
        
        
    }


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
            buffer = buffer.Slice(_end.Length);
        }

        //implicit conversions        
        public static implicit operator NatsMsgHeaders(Dictionary<string,string> headers) => new NatsMsgHeaders(headers);
        public static implicit operator NatsMsgHeaders(List<KeyValuePair<string, string>> headers) => new NatsMsgHeaders(headers);
        public static implicit operator NatsMsgHeaders(KeyValuePair<string, string>[] headers) => new NatsMsgHeaders(headers);

    }

}