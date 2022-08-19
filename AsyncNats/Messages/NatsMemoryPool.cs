namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;
    using System.Runtime.CompilerServices;
    using System.Text;

    public sealed class NatsMemoryPool 
    {
        private readonly ArrayPool<byte> _pool;

        public NatsMemoryPool(ArrayPool<byte>? pool = null)
        {
            _pool = pool ?? ArrayPool<byte>.Create(1024 * 1024, 256);
        }

        public NatsMemoryOwner Rent(int minBufferSize = -1) => new NatsMemoryOwner(this, minBufferSize);

        public int MaxBufferSize => 1024 * 1024;

        public readonly struct NatsMemoryOwner : IMemoryOwner<byte>
        {
            private readonly NatsMemoryPool _owner;

            private readonly byte[] _buffer;
            public NatsMemoryOwner(NatsMemoryPool owner, int length)
            {
                _owner = owner;
                _buffer = owner._pool.Rent(length);

                Memory = _buffer.AsMemory(0, length <= 0 ? _owner.MaxBufferSize : length);
            }

            public Memory<byte> Memory { get; }
            public void Dispose() => _owner._pool.Return(_buffer);
        }

        
    }

    /// <summary>
    /// A simple struct implementation of IMemoryOwner<typeparamref name="T"/> that does not return to a pool
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public readonly struct NoOwner<T> : IMemoryOwner<T>
    {
        public Memory<T> Memory => _memory;
        
        
        readonly Memory<T> _memory;

        public NoOwner(Memory<T> memory)
        {
            _memory = memory;
        }

        public void Dispose()
        {
            //this is safe because structs are not passed by reference
        }
    }

    public readonly struct Utf8String:IEquatable<Utf8String>,IEquatable<string>
    {
        public static Utf8String Empty = new Utf8String(string.Empty);
        public bool IsEmpty => Memory.Length == 0;

        public readonly ReadOnlyMemory<byte> Memory;
        private readonly string _string;

        private readonly int _hashCode;

        public Utf8String(ReadOnlyMemory<byte> value,bool convert=true)
        {
            Memory = value;
            _string = convert ? Encoding.UTF8.GetString(value.Span) : string.Empty;
            _hashCode = Utf8String.ComputeHashCode(Memory.Span);
        }

        public Utf8String(string value)
        {
            _string = value ?? string.Empty;           
            Memory = (_string == string.Empty) ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(value); 
            _hashCode = Utf8String.ComputeHashCode(Memory.Span);
        }

        public string AsString()
        {
            return _string.Length>0 ? _string : Memory.Span.Length==0 ? string.Empty : Encoding.UTF8.GetString(Memory.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in Utf8String other)
        {
            return other.Memory.Span.SequenceEqual(this.Memory.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in ReadOnlySpan<byte> other)
        {
            return other.SequenceEqual(this.Memory.Span);
        }

        public bool Equals(Utf8String other)
        {
            return Equals(in other);
        }

        public bool Equals(string other)
        {
            return this.AsString() == other;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        private static int ComputeHashCode(ReadOnlySpan<byte> span)
        {
            var hash = new HashCode();
            for (var i = span.Length - 1; i >= 0; i--)
                hash.Add(span[i]);

            return hash.ToHashCode();
        }

        public static implicit operator Utf8String(string value) => new Utf8String(value);
        
    }
}