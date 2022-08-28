namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;



    internal class NatsPublishBuffer
    {
        public event Action? OnCommit;
        public bool IsDetached { get; private set; }
        public int Messages => _messages;
        public byte[] Buffer => _buffer;
        public int Available => _length - _position;
        public ReadOnlyMemory<byte> GetMemory() => _buffer.AsMemory().Slice(0,_position);

        readonly byte[] _buffer;

        readonly object _lock = new object();
        
        readonly int _length;

        int _messages;
        int _position;
        bool _commit;
        int _writers;

        public NatsPublishBuffer(byte[] buffer,int length=-1, bool detached=false)
        {            
            _length =length>0 ?length: buffer.Length;
            _buffer = buffer;
            IsDetached = detached;
        }

        internal NatsPublishBuffer(byte[] buffer,int position,int messages)
        {
            _length = buffer.Length;
            _buffer = buffer;
            _position=position;
            _messages = messages;
        }

        public bool TryWrite(int serializedLength,SerializeDelegate writeToBuffer,out int messageIndex)
        {
            messageIndex = -1;
            int start, end;
            lock (_lock)
            {
                var available = (_length - _position);

                if (available < serializedLength) return false;

                if (_commit) return false;

                Interlocked.Increment(ref _writers);

                //get a slot
                start = _position;
                end = _position + serializedLength;
                messageIndex = _messages;
                Interlocked.Increment(ref _messages);

                //advance position
                _position = end;
            }

            var  writeSlot= _buffer.AsSpan().Slice(start, serializedLength);

            writeToBuffer(writeSlot);
            Interlocked.Decrement(ref _writers);
            return true;
        }


        public async ValueTask Commit()
        {
            bool commited = _commit==false;
            lock (_lock)
            {
                _commit = true;
            }

            if(commited)
                OnCommit?.Invoke();

            if (_writers == 0) return;

            SpinWait.SpinUntil(() => _writers == 0, 1);

            while (_writers > 0)
                await Task.Yield();
        }
        
        public void Reset()
        {
            lock (_lock)
            {
                _position = 0;
                _writers = 0;
                _messages = 0;
                _commit = false;
            }
            
        }
    }

}