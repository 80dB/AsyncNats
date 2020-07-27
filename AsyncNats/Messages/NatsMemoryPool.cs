namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Buffers;

    public sealed class NatsMemoryPool : MemoryPool<byte>
    {
        private class NatsMemoryOwner : IMemoryOwner<byte>
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

        private ArrayPool<byte> _pool;

        public NatsMemoryPool(ArrayPool<byte>? pool = null)
        {
            _pool = pool ?? ArrayPool<byte>.Create(1024 * 1024, 256);
        }

        protected override void Dispose(bool disposing)
        { }

        public override IMemoryOwner<byte> Rent(int minBufferSize = -1) => new NatsMemoryOwner(this, minBufferSize);
        public override int MaxBufferSize => 1024 * 1024;
    }
}