namespace EightyDecibel.AsyncNats
{
    using EightyDecibel.AsyncNats.Messages;
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
        public ReadOnlyMemory<byte> GetMemory() => _buffer.AsMemory().Slice(0, _position);

        private readonly byte[] _buffer;
        private readonly object _lock = new object();
        private readonly int _length;
        private int _messages;
        private int _position;
        private bool _commit;
        private int _writers;

        public NatsPublishBuffer(byte[] buffer, int length = -1, bool detached = false)
        {
            _length = length > 0 ? length : buffer.Length;
            _buffer = buffer;
            IsDetached = detached;
        }

        internal NatsPublishBuffer(byte[] buffer, int position, int messages)
        {
            _length = buffer.Length;
            _buffer = buffer;
            _position = position;
            _messages = messages;
        }

        public bool TryWrite<T>(T msg, out int messageIndex) where T : INatsClientMessage
        {
            messageIndex = -1;
            int start;
            lock (_lock)
            {
                if ((_length - _position) < msg.Length || _commit) return false;

                Interlocked.Increment(ref _writers);

                //get a slot
                start = _position;
                _position += msg.Length;
                messageIndex = _messages;
                _messages++;
            }

            var writeSlot = _buffer.AsSpan().Slice(start, msg.Length);

            msg.Serialize(writeSlot);
            Interlocked.Decrement(ref _writers);
            return true;
        }


        public async ValueTask Commit()
        {
            bool commited = _commit == false;
            lock (_lock)
            {
                _commit = true;
            }

            if (commited)
                OnCommit?.Invoke();

            var count = 2048;
            while (_writers > 0 && count >= 0)
            {
                count--;
            }

            while (_writers > 0)
            {
                await Task.Yield();
            }
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