namespace EightyDecibel.AsyncNats
{
    using EightyDecibel.AsyncNats.Messages;
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;


    public delegate void SerializeDelegate(Span<byte> writeBuffer);

    internal class NatsPublishChannel
    {
        public int DefaultBufferLength { get; set; } = 1024 * 64;

        public ChannelReader<NatsPublishBuffer> Reader => _channel.Reader;
        public ChannelWriter<NatsPublishBuffer> Writer => _channel.Writer;


        private readonly Channel<NatsPublishBuffer> _channel;
        private readonly NatsMemoryPool _natsMemoryPool;
        private readonly object _lock = new object();
        private readonly ConcurrentBag<NatsPublishBuffer> _pool = new ConcurrentBag<NatsPublishBuffer>();
        private NatsPublishBuffer _current;
        private int _version = 0;

        public NatsPublishChannel(NatsMemoryPool pool, CancellationToken cancellationToken)
        {
            _natsMemoryPool = pool;
            _channel = Channel.CreateBounded<NatsPublishBuffer>(new BoundedChannelOptions(Math.Min(2, Environment.ProcessorCount)) { AllowSynchronousContinuations = false });
            _current = GetBuffer();
        }

        public ValueTask Publish<T>(T msg, CancellationToken cancellationToken) where T : INatsClientMessage
        {
            return PublishInternal(msg, cancellationToken);
        }

        public async ValueTask PublishInternal<T>(T msg, CancellationToken cancellationToken) where T : INatsClientMessage
        {
            var current = _current;
            var currentVersion = _version;
            int messageIndex;
            
            while (!current.TryWrite(msg, out messageIndex))
            {
                await _channel.Writer.WaitToWriteAsync().ConfigureAwait(false);

                lock (_lock)
                {
                    if (currentVersion == _version)
                    {
                        current = GetBuffer(msg.Length);
                        current.Reset();
                        _current = current;
                        _version++;
                    }
                }

                current = _current;
                currentVersion = _version;
            }

            //at this point message was written
            if (messageIndex == 0)
            {
                //if wrote first message on buffer, enqueue
                await _channel.Writer.WriteAsync(current, cancellationToken).ConfigureAwait(false);
            }
        }


        public void Return(NatsPublishBuffer buffer)
        {
            if (!buffer.IsDetached && _pool.Count < Environment.ProcessorCount)
            {
                _pool.Add(buffer);
            }
            else
            {
                _natsMemoryPool.ReturnBuffer(buffer.Buffer);
            }
        }

        private NatsPublishBuffer GetBuffer(int? minimumSize = null)
        {
            var bufferLength = DefaultBufferLength;
            if (minimumSize.HasValue && minimumSize > bufferLength)
            {
                return new NatsPublishBuffer(_natsMemoryPool.RentBuffer(minimumSize.Value), detached: true);
            }

            if (!_pool.TryTake(out var buffer))
                buffer = new NatsPublishBuffer(_natsMemoryPool.RentBuffer(bufferLength), bufferLength, detached: false);

            return buffer;
        }



    }


}