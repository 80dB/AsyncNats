namespace EightyDecibel.AsyncNats.Rpc
{
    using System;

    internal class NatsServerResponse
    {
        public byte[]? E { get; set; }
    }

    internal class NatsServerResponse<TResult>
    {
        public byte[]? E { get; set; }
#nullable disable
        public TResult R { get; set; } = default;
#nullable restore

        public NatsServerResponse()
        { }

        public NatsServerResponse(TResult result)
        {
            R = result;
        }
    }
}
