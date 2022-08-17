using Microsoft.Extensions.Logging;

namespace EightyDecibel.AsyncNats.Rpc
{
    using EightyDecibel.AsyncNats.Messages;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    internal class NatsServerProxy<TContract>
    {
        protected readonly NatsConnection _parent;
        protected readonly INatsSerializer _serializer;
        protected readonly TContract _contract;

        protected readonly string _subject;
        protected readonly string? _queueGroup;

        protected readonly ILogger? _logger;

        private Dictionary<string, (InvokeAsyncDelegate invoke, SerializeDelegate serialize)> _asyncMethods;
        private Dictionary<string, InvokeDelegate> _syncMethods;
        private TaskScheduler? _taskScheduler;

        internal NatsServerProxy(NatsConnection parent, string subject, string? queueGroup, INatsSerializer serializer, TContract contract, TaskScheduler? taskScheduler, IReadOnlyDictionary<string, (MethodInfo invoke, MethodInfo serialize)> asyncMethods, IReadOnlyDictionary<string, MethodInfo> syncMethods)
        {
            _parent = parent;
            _logger = _parent.Options.LoggerFactory?.CreateLogger($"EightyDecibel.AsyncNats.Rpc.NatsServerProxy<{typeof(TContract).Name}>");
            _serializer = serializer;
            _contract = contract;
            _taskScheduler = taskScheduler;

            _subject = subject;
            _queueGroup = queueGroup;

            _asyncMethods = new Dictionary<string, (InvokeAsyncDelegate invoke, SerializeDelegate serialize)>();
            foreach (var asyncMethod in asyncMethods)
            {
                var invoke = (InvokeAsyncDelegate)Delegate.CreateDelegate(typeof(InvokeAsyncDelegate), this, asyncMethod.Value.invoke);
                var serialize = (SerializeDelegate)Delegate.CreateDelegate(typeof(SerializeDelegate), this, asyncMethod.Value.serialize);
                _asyncMethods.Add(asyncMethod.Key, (invoke, serialize));
            }

            _syncMethods = new Dictionary<string, InvokeDelegate>();
            foreach (var syncMethod in syncMethods)
            {
                var invoke = (InvokeDelegate)Delegate.CreateDelegate(typeof(InvokeDelegate), this, syncMethod.Value);
                _syncMethods.Add(syncMethod.Key, invoke);
            }
        }

        public async Task Listener(CancellationToken cancellationToken = default)
        {
            _logger?.LogTrace("Starting contract server listener");
            var taskFactory = _taskScheduler != null ? new TaskFactory(_taskScheduler) : null;
            await foreach(var msg in _parent.Subscribe(_subject, _queueGroup, cancellationToken))
            {
                // Perform another rent, the invoke's will release
                msg.Rent();
                try
                {
                    var method = msg.Subject.AsString().Substring((_subject ?? string.Empty).Length - 1);
                    if (_asyncMethods.TryGetValue(method, out var delegates))
                    {
                        if (taskFactory == null) await InvokeAsync(method, delegates.invoke, delegates.serialize, msg, cancellationToken);
#pragma warning disable 4014
                        else taskFactory.StartNew(() => InvokeAsync(method, delegates.invoke, delegates.serialize, msg, cancellationToken), cancellationToken);
#pragma warning restore 4014
                    }
                    else if (_syncMethods.TryGetValue(method, out var invoke))
                    {
                        if (taskFactory == null) await InvokeSync(method, invoke, msg, cancellationToken);
#pragma warning disable 4014
                        else taskFactory.StartNew(() => InvokeSync(method, invoke, msg, cancellationToken), cancellationToken);
#pragma warning restore 4014
                    }
                    else
                    {
                        throw new KeyNotFoundException("Unknown method");
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Catch remaining exceptions (shouldn't be any) and pass them 
                    _logger?.LogError(ex, "Exception in contract server listener");
                    _parent.ServerException(this, msg, ex);
                }
            }
            _logger?.LogTrace("Exited contract server listener");
        }

        private async Task InvokeSync(string method, InvokeDelegate invoke, NatsMsg msg, CancellationToken cancellationToken)
        {
            try
            {
                var response = invoke(msg.Payload);
                if (!string.IsNullOrEmpty(msg.ReplyTo.AsString()))
                    await _parent.PublishAsync(msg.ReplyTo.AsString(), response, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "{Method} threw an exception", method);

                if (!string.IsNullOrEmpty(msg.ReplyTo.AsString()))
                {
                    await using var ms = new MemoryStream();
                    var formatter = new BinaryFormatter();
                    formatter.Serialize(ms, ex);

                    await _parent.PublishObjectAsync(msg.ReplyTo.AsString(), new NatsServerResponse {E = ms.ToArray()}, cancellationToken: cancellationToken);
                }

                _parent.ServerException(this, msg, ex);
            }
            finally
            {
                _logger?.LogTrace("{Method} finished", method);
                msg.Release();
            }
        }

        private async Task InvokeAsync(string method, InvokeAsyncDelegate invoke, SerializeDelegate serialize, NatsMsg msg, CancellationToken cancellationToken)
        {
            try
            {
                var task = invoke(msg.Payload);
                await task;
                var response = serialize(task);
                if (!string.IsNullOrEmpty(msg.ReplyTo.AsString()))
                    await _parent.PublishAsync(msg.ReplyTo.AsString(), response, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "{Method} threw an exception", method);

                if (!string.IsNullOrEmpty(msg.ReplyTo.AsString()))
                {
                    await using var ms = new MemoryStream();
                    var formatter = new BinaryFormatter();
                    formatter.Serialize(ms, ex);

                    await _parent.PublishObjectAsync(msg.ReplyTo.AsString(), new NatsServerResponse { E = ms.ToArray() }, cancellationToken: cancellationToken);
                }

                _parent.ServerException(this, msg, ex);
            }
            finally
            {
                _logger?.LogTrace("{Method} finished", method);
                msg.Release();
            }
        }
    }
}
