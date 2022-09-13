using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using EightyDecibel.AsyncNats.Messages;

namespace EightyDecibel.AsyncNats
{
    public class NatsPubBuffer
    {
        private object _syncLock = new object();

        private BlockingCollection<(byte[] buffer, int length, int count)> _sendQueue = new BlockingCollection<(byte[] buffer, int length, int count)>();
        private BlockingCollection<byte[]> _pool = new BlockingCollection<byte[]>();

        private const int _bufferLength = 2 * 1024 * 1024;
        
        private byte[] _writeBuffer;
        private int _writeBufferLength;
        private int _writeMessagesCount;

        public NatsPubBuffer()
        {
            for(var i = 0; i < 10; i ++) _pool.Add(new byte[_bufferLength]);
            _writeBuffer = new byte[_bufferLength];
        }

        public (byte[] buffer, int length, int count) GetReadBuffer()
        {
            if (_sendQueue.TryTake(out var result)) return result;
            lock (_syncLock)
            {
                if (_writeBufferLength == 0) return (Array.Empty<byte>(), 0, 0);

                result = (_writeBuffer, _writeBufferLength, _writeMessagesCount);

                _writeBuffer = _pool.Take();
                _writeBufferLength = 0;
                _writeMessagesCount = 0;
            }

            return result;
        }

        public void ReleaseBuffer(byte[] buffer)
        {
            _pool.Add(buffer);
        }

        public void Serialize(INatsClientMessage message)
        {
            while (true)
            {
                lock (_syncLock)
                {
                    var remaining = _bufferLength - _writeBufferLength;
                    if (message.Length < remaining)
                    {
                        message.Serialize(_writeBuffer.AsSpan(_writeBufferLength));
                        _writeBufferLength += message.Length;
                        _writeMessagesCount++;
                        break;
                    }

                    _sendQueue.Add((_writeBuffer, _writeBufferLength, _writeMessagesCount));

                    _writeBuffer = _pool.Take();
                    _writeBufferLength = 0;
                    _writeMessagesCount = 0;
                }
            }
        }
    }
}