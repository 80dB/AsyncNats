using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.IO.Pipelines;
    using System.Net;
    using EightyDecibel.AsyncNats.Messages;

    public class NatsDefaultOptions : INatsOptions
    {
        public NatsDefaultOptions()
        {
            Server = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 4222);
            SenderQueueLength = 5000;
            ReceiverQueueLength = 5000;
            Serializer = new NatsDefaultSerializer();
            ArrayPool = ArrayPool<byte>.Create(1024*1024, 1024);


            using var random = RandomNumberGenerator.Create();
            Span<byte> bytes = stackalloc byte[16];
            random.GetBytes(bytes);
            RequestPrefix = new Guid(bytes).ToString();
        }

        public IPEndPoint Server { get; set; }
        public int SenderQueueLength { get; set; }
        public int ReceiverQueueLength { get; set; }
        public ArrayPool<byte> ArrayPool { get; set; }
        public INatsSerializer Serializer { get; set; }
        public bool Verbose { get; set; }
        public string? AuthorizationToken { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool Echo { get; set; }

        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public string RequestPrefix { get; set; }

        public ILoggerFactory? LoggerFactory { get; set; }
    }
}