using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EightyDecibel.AsyncNats.Messages;
using Microsoft.Extensions.Logging;

namespace EightyDecibel.AsyncNats
{
    public class NatsRequestResponse
    {
        private static long _requestCount;
        
        private readonly ILogger<NatsRequestResponse>? _logger;
        private readonly string _subject;
        private readonly INatsConnection _connection;
        private readonly object _syncLock = new object();
        private readonly CancellationTokenSource _disposeTokenSource = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, Action<NatsMsg>> _responseHandlers = new ConcurrentDictionary<string, Action<NatsMsg>>();

        private Task? _listener;
        
        public NatsRequestResponse(INatsConnection connection)
        {
            _connection = connection;
            _logger = _connection.Options.LoggerFactory?.CreateLogger<NatsRequestResponse>();

            _subject = connection.Options.RequestPrefix;
        }

        private void StartListener()
        {
            if (_listener != null) return;
            lock (_syncLock)
            {
                if (_listener != null) return;

                _listener = Task.Run(Listener);
            }
        }

        private async Task Listener()
        {
            _logger?.LogTrace("Starting response listener");
            
            var cancellationToken = _disposeTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var subscription = _connection.Subscribe($"{_subject}.>", cancellationToken: cancellationToken);
                    await foreach (var message in subscription.WithCancellation(cancellationToken))
                    {
                        if (!_responseHandlers.TryRemove(message.Subject.AsString(), out var handler))
                            continue;

                        handler(message);
                    }
                }
                catch (Exception e)
                {
                    _logger?.LogError(e, "Response listener threw an exception");

                    (_connection as NatsConnection)?.ServerException(this, null!, e);
                }
            }

            _logger?.LogTrace("Exited response listener");
        }

        internal async Task<TResponse> InternalRequest<TResponse>(string subject, Memory<byte> request, Func<NatsMsg, TResponse> deserialize, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            // First start the listener if it's not listening yet
            StartListener();
            
            // Combine cancellation token with timeout
            using var timeoutSource = new CancellationTokenSource(timeout ?? _connection.Options.RequestTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken);
            var linkedCancellationToken = linkedSource.Token;
            
            var replyTo = $"{_subject}.{Interlocked.Increment(ref _requestCount)}";
            
            var taskSource = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _responseHandlers[replyTo] = msg =>
            {
                try
                {
                    taskSource.TrySetResult(deserialize(msg));
                }
                catch (Exception e)
                {
                    taskSource.TrySetException(e);
                }
            };
            
            await using var registration =
                linkedCancellationToken.Register(() =>
                {
                    taskSource.TrySetCanceled(linkedCancellationToken);
                    _responseHandlers.TryRemove(replyTo, out _);
                });

            await _connection.PublishMemoryAsync(subject, request, replyTo, linkedCancellationToken);
            return await taskSource.Task;
        }
    }
}