namespace EightyDecibel.AsyncNats
{
    using System;
    using System.IO.Pipelines;
    using System.Net;

    public class NatsDefaultOptions : INatsOptions
    {
        public IPEndPoint Server { get; set; } = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 4222);
        public int SenderQueueLength { get; set; } = 5000;
        public PipeOptions SenderPipeOptions { get; set; } = new PipeOptions();
        public int ReceiverQueueLength { get; set; } = 5000;
        public PipeOptions ReceiverPipeOptions { get; set; } = new PipeOptions();
        public int FlushAtLeastEvery { get; set; } = 500;
        public INatsSerializer Serializer { get; set; } = new NatsDefaultSerializer();
        public bool Verbose { get; set; }
        public string? AuthorizationToken { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool Echo { get; set; }

        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public string RequestPrefix { get; set; } = $"{Environment.TickCount}";
    }
}