namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    public interface INatsClientMessage
    {
        int Length { get; }
        void Serialize(Span<byte> buffer);

    }

}