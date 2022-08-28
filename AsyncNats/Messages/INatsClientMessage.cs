using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace EightyDecibel.AsyncNats.Messages
{
    public interface INatsClientMessage
    {
        int Length { get; }
        void Serialize(Span<byte> buffer);

    }

}