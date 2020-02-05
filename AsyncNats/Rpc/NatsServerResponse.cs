namespace EightyDecibel.AsyncNats.Rpc
{
    using System;
    using System.Runtime.Serialization;

    [DataContract, Serializable]
    public class NatsServerResponse
    {
        [DataMember(Order = 1)]
        public byte[]? E { get; set; }
    }

    [DataContract, Serializable]
    public class NatsServerResponse<TResult>
    {
        [DataMember(Order = 1)]
        public byte[]? E { get; set; }
#nullable disable
        [DataMember(Order = 2)]
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
