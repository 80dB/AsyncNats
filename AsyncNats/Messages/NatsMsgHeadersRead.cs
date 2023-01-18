namespace EightyDecibel.AsyncNats.Messages
{
    using System;
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

        public bool TryGetValue(ReadOnlySpan<byte> key, out ReadOnlyMemory<byte> value)
        {
            value = ReadOnlyMemory<byte>.Empty;

            foreach (var (k, v) in _headers)
            {                
                if (k.Span.SequenceEqual(key))
                {
                    value = v;
                    return true;
                }
            }

            return false;
        }
        public bool TryGetValue(string key, out ReadOnlyMemory<byte> value)
        {
            value = null;

            Span<byte> utf8Key = key.Length < 1024 ? stackalloc byte[Encoding.UTF8.GetMaxByteCount(key.Length)]
                : new byte[Encoding.UTF8.GetMaxByteCount(key.Length)];

            var chars = Encoding.UTF8.GetBytes(key, utf8Key);
            utf8Key = utf8Key.Slice(0, chars);

            foreach (var (k, v) in _headers)
            {
                if (k.Span.SequenceEqual(utf8Key))
                {
                    value = v;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValueAsString(ReadOnlySpan<byte> key, out string? value)
        {
            value = null;

            foreach (var (k, v) in _headers)
            {
                if (k.Span.SequenceEqual(key))
                {
                    value = Encoding.UTF8.GetString(v.Span);
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValueAsString(string key, out string? value)
        {
            value = null;

            Span<byte> utf8Key = key.Length < 1024 ? stackalloc byte[Encoding.UTF8.GetMaxByteCount(key.Length)]
                : new byte[Encoding.UTF8.GetMaxByteCount(key.Length)];
            
            var chars = Encoding.UTF8.GetBytes(key, utf8Key);
            utf8Key = utf8Key.Slice(0, chars);

            foreach (var (k, v) in _headers)
            {
                if (k.Span.SequenceEqual(utf8Key))
                {
                    value = Encoding.UTF8.GetString(v.Span);
                    return true;
                }
            }

            return false;
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


}