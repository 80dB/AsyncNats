using Microsoft.Extensions.Logging;

namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Net;

    public interface INatsOptions
    {
        IPEndPoint Server { get; }

        int SenderQueueLength { get; }

        int ReceiverQueueLength { get; }

        ArrayPool<byte> ArrayPool { get; }

        INatsSerializer Serializer { get; }

        bool Verbose { get; }

        string? AuthorizationToken { get; }
        string? Username { get; }
        string? Password { get; }

        bool Echo { get; }

        TimeSpan RequestTimeout { get; }
        string RequestPrefix { get; }

        ILoggerFactory? LoggerFactory { get; }
    }
}