namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text;
    
    public readonly struct NatsKey : IEquatable<NatsKey>, IEquatable<string>
    {
        public static NatsKey Empty = new NatsKey(ReadOnlyMemory<byte>.Empty);
        public bool IsEmpty => Memory.Length == 0;

        public readonly ReadOnlyMemory<byte> Memory;
        private readonly string _string;

        public NatsKey(ReadOnlyMemory<byte> value) : this(value, false)
        { }

        internal NatsKey(ReadOnlyMemory<byte> value, bool convert = false)
        {
            Memory = value;
            _string = convert ? Encoding.UTF8.GetString(value.Span) : string.Empty;
        }

        public NatsKey(string? value)
        {
            _string = value ?? string.Empty;
            Memory = (_string == string.Empty) ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(value);
        }

        public string AsString()
        {
            return _string.Length > 0 ? _string : Memory.Span.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Memory.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in NatsKey other)
        {
            return other.Memory.Span.SequenceEqual(this.Memory.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in ReadOnlySpan<byte> other)
        {
            return other.SequenceEqual(this.Memory.Span);
        }

        public bool Equals(NatsKey other)
        {
            return Equals(in other);
        }

        public bool Equals(string other)
        {
            return this.AsString() == other;
        }

        public override int GetHashCode()
        {
            //TODO Should cache?
            return ComputeHashCode(Memory.Span);
        }

        private static int ComputeHashCode(ReadOnlySpan<byte> span)
        {
            var hash = new HashCode();
            for (var i = span.Length - 1; i >= 0; i--)
                hash.Add(span[i]);

            return hash.ToHashCode();
        }

        public override string ToString()
        {
            return AsString();
        }

        public static implicit operator NatsKey(string value) => string.IsNullOrEmpty(value) ? NatsKey.Empty : new NatsKey(value);

        public static implicit operator NatsKey(ReadOnlyMemory<byte> value) => new NatsKey(value);

        public static implicit operator NatsKey(byte[] value) => new NatsKey(value);
    }


}

