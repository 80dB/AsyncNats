namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Buffers.Text;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class NatsMsgHeaders
    {
        public static readonly NatsMsgHeaders Empty = new NatsMsgHeaders();

        private static readonly ReadOnlyMemory<byte> _protocolVersion = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("NATS/1.0\r\n"));
        private static readonly ReadOnlyMemory<byte> _separator = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(":"));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));
                

        private IEnumerable<KeyValuePair<string, string>>? _stringHeaders = null;        
        private readonly IEnumerable<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> _headers;

        public NatsMsgHeaders(ReadOnlyMemory<byte> data)
        {
            if (data.IsEmpty)
                _headers = Enumerable.Empty<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>>();
            else
                _headers = ParseHeaders(data);
        }
        private NatsMsgHeaders()
        {
            _headers = Enumerable.Empty<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> ();
        }
        
        public IEnumerable<KeyValuePair<string, string>> ReadAsString()
        {
            if (_stringHeaders != null)
                return _stringHeaders;

            if (_headers.Count()>0)
            {
                _stringHeaders = _headers
                    .Select(x => new KeyValuePair<string, string>(Encoding.UTF8.GetString(x.Key.Span), Encoding.UTF8.GetString(x.Value.Span)));

                return _stringHeaders;
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

            data = data.Slice(_end.Length + _protocolVersion.Length);

            while (data.Length > 0)
            {
                var keyEnd = data.Span.IndexOf(_separator.Span);
                var key = data.Slice(0, keyEnd);
                data = data.Slice(keyEnd + 1);

                var valueEnd = data.Span.IndexOf(_end.Span);
                var value = data.Slice(0, valueEnd);
                data = data.Slice(valueEnd + 1);

                headers.Add(new KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>(key, value));
            }

            return headers;

        }
        
        
    }


    public readonly struct NatsMsgHeadersPublish
    {
        private static readonly ReadOnlyMemory<byte> _protocolVersion = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("NATS/1.0\r\n"));
        private static readonly ReadOnlyMemory<byte> _separatorWithSpace = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(": "));
        private static readonly ReadOnlyMemory<byte> _end = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("\r\n"));

        public readonly int SerializedLength;

        private readonly IEnumerable<KeyValuePair<string, string>> _headersWrite;

        public NatsMsgHeadersPublish(IEnumerable<KeyValuePair<string, string>> headers)
        {
            _headersWrite = headers;
            SerializedLength = _protocolVersion.Length;
            foreach (var (k, v) in headers)
            {
                SerializedLength += k.Length + v.Length + _separatorWithSpace.Length + _end.Length;
            }
        }

        

        internal void SerializeTo(Span<byte> buffer)
        {

            _protocolVersion.Span.CopyTo(buffer);
            buffer = buffer.Slice(_protocolVersion.Length);

            foreach (var (k, v) in _headersWrite!)
            {
                Encoding.UTF8.GetBytes(k, buffer);
                buffer = buffer.Slice(k.Length);

                _separatorWithSpace.Span.CopyTo(buffer);
                buffer = buffer.Slice(_separatorWithSpace.Length);

                Encoding.UTF8.GetBytes(v, buffer);
                buffer = buffer.Slice(v.Length);

                _end.Span.CopyTo(buffer);
                buffer = buffer.Slice(_end.Length);
            }
        }

        //implicit conversions        
        public static implicit operator NatsMsgHeadersPublish(Dictionary<string,string> headers) => new NatsMsgHeadersPublish(headers);
        public static implicit operator NatsMsgHeadersPublish(List<KeyValuePair<string, string>> headers) => new NatsMsgHeadersPublish(headers);
        public static implicit operator NatsMsgHeadersPublish(KeyValuePair<string, string>[] headers) => new NatsMsgHeadersPublish(headers);

    }

}