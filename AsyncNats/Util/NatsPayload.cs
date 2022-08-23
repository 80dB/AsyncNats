namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text;

    public readonly struct NatsPayload : IEquatable<NatsPayload>
    {
        public static NatsPayload Empty = new NatsPayload(ReadOnlyMemory<byte>.Empty);
        public bool IsEmpty => Memory.Length == 0;

        public readonly ReadOnlyMemory<byte> Memory;
        private readonly string _string;

        public NatsPayload(ReadOnlyMemory<byte> value)
        {
            Memory = value;
            _string = string.Empty;
        }

        public NatsPayload(string? value)
        {
            _string = value ?? string.Empty;
            Memory = _string == string.Empty ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(value);
        }

        public string AsString()
        {
            return _string.Length > 0 ? _string : Memory.Span.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Memory.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in NatsPayload other)
        {
            return other.Memory.Span.SequenceEqual(this.Memory.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in ReadOnlySpan<byte> other)
        {
            return other.SequenceEqual(this.Memory.Span);
        }

        public bool Equals(NatsPayload other)
        {
            return Equals(in other);
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
        
        public static implicit operator NatsPayload(string value) => new NatsPayload(value);
        public static implicit operator NatsPayload(ReadOnlyMemory<byte> value) => new NatsPayload(value);
        public static implicit operator NatsPayload(byte[] value) => new NatsPayload(value);
    }
}