namespace EightyDecibel.AsyncNats
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

    

}