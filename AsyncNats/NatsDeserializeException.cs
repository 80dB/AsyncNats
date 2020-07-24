namespace EightyDecibel.AsyncNats
{
    using System;
    using EightyDecibel.AsyncNats.Messages;

    public class NatsDeserializeException : Exception
    {
        public NatsMsg Msg { get; }
        
        public NatsDeserializeException(NatsMsg msg, Exception innerException)
            : base(innerException.Message, innerException)
        {
            Msg = msg;
        }
    }
}