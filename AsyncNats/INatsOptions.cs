using Microsoft.Extensions.Logging;

namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Net;
    using System.Threading.Tasks;

    public interface INatsOptions
    {
        [Obsolete("Use Servers instead")]
        IPEndPoint? Server { get; }

        string[] Servers { get; set; }

        /// <summary>
        /// If set, will be used instead of system dns
        /// </summary>
        Func<string, Task<IPAddress[]>> DnsResolver { get; set; }

        NatsServerPoolFlags ServersOptions { get; set; }

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