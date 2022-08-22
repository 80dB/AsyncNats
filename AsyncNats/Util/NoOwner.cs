namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks.Sources;


    /// <summary>
    /// A simple struct implementation of IMemoryOwner<typeparamref name="T"/> that does not return to a pool
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal readonly struct NoOwner<T> : IMemoryOwner<T>
    {
        public Memory<T> Memory => _memory;        
        
        readonly Memory<T> _memory;

        readonly Action? _onDisposed;
        public NoOwner(Memory<T> memory)
        {
            _memory = memory;
            _onDisposed = null;
        }

        internal NoOwner(ReadOnlyMemory<T> memory,Action onDisposed=null)
        {
            //unsafe, but we know this is only used internally with no intention to write on memory
            _memory = MemoryMarshal.AsMemory(memory);

            _onDisposed = onDisposed;
        }

        public void Dispose()
        {
            _onDisposed?.Invoke();
            //this is safe because structs are not passed by reference
        }

     
    }


}