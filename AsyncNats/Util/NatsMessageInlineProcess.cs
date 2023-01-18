namespace EightyDecibel.AsyncNats
{
    using EightyDecibel.AsyncNats.Messages;

    public delegate void NatsMessageInlineProcess(ref NatsInlineMsg message);
}
