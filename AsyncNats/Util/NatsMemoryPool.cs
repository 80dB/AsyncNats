namespace EightyDecibel.AsyncNats
{
    using System.Buffers;

    public sealed class NatsMemoryPool 
    {
        private readonly ArrayPool<byte> _pool;

        private static readonly ArrayPool<byte> _shared = ArrayPool<byte>.Create(1024 * 1024, 256);

        public NatsMemoryPool(ArrayPool<byte>? pool = null)
        {
            _pool = pool ?? _shared;
        }

        internal NatsMemoryOwner Rent(int minBufferSize) => new NatsMemoryOwner(_pool, minBufferSize);

        internal byte[] RentBuffer(int minBufferSize)=> _pool.Rent(minBufferSize);

        internal void ReturnBuffer(byte[] buffer) => _pool.Return(buffer);

    }

}