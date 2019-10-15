namespace EightyDecibel.AsyncNats
{
    using System.IO.Pipelines;
    using System.Net;

    public interface INatsOptions
    {
        IPEndPoint Server { get; }

        int SenderQueueLength { get; }
        PipeOptions SenderPipeOptions { get; }

        int ReceiverQueueLength { get; }
        PipeOptions ReceiverPipeOptions { get; }

        int FlushAtLeastEvery { get; }

        INatsSerializer Serializer { get; }
    }
}