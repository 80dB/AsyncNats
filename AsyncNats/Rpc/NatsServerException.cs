namespace EightyDecibel.AsyncNats.Rpc
{
    using System;
    using EightyDecibel.AsyncNats.Messages;

    public class NatsServerException : Exception
    {
        public NatsMsg Msg { get; }
        
        public NatsServerException(NatsMsg msg, Exception innerException)
            : base(innerException.Message, innerException)
        {
            Msg = msg;
        }
    }
}