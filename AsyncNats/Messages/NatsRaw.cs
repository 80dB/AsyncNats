using System;
using System.Threading.Tasks;

namespace EightyDecibel.AsyncNats.Messages
{
    public class NatsRaw : INatsClientMessage, IAsyncDisposable
    {
        private readonly ReadOnlyMemory<byte> _rawData;
        private readonly TaskCompletionSource<bool> _taskCompletionSource;

        public NatsRaw(ReadOnlyMemory<byte> rawData)
        {
            _rawData = rawData;
            _taskCompletionSource = new TaskCompletionSource<bool>();

            Length = rawData.Length;
        }

        public int Length { get; }

        public void Serialize(Span<byte> buffer)
        {
            _rawData.Span.CopyTo(buffer);
            _taskCompletionSource.SetResult(true);
        }

        public async ValueTask DisposeAsync()
        {
            await _taskCompletionSource.Task;
        }
    }
}