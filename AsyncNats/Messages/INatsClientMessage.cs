using System;

namespace EightyDecibel.AsyncNats.Messages
{
    public interface INatsClientMessage
    {
        int Length { get; }
        void Serialize(Span<byte> buffer);
    }
}