namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;



    public readonly struct NatsMemoryOwner
    {
        public readonly Memory<byte> Memory;

        private readonly ArrayPool<byte>? _owner;        

        private readonly byte[]? _buffer;

        internal NatsMemoryOwner(ArrayPool<byte> owner, int length)
        {
            _owner = owner;
            _buffer = owner.Rent(length);
            Memory = _buffer.AsMemory(0, length);
        }

        internal NatsMemoryOwner(byte[] buffer)
        {
            _owner = null;
            _buffer = null;
            Memory = buffer.AsMemory();
        }
       
        public void Return()
        {            
            if (_owner is not null)
            {
                _owner.Return(_buffer);
            }
        }

    }


}