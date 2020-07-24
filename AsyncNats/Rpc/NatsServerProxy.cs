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

        private Dictionary<string, (InvokeAsyncDelegate invoke, SerializeDelegate serialize)> _asyncMethods;
        private Dictionary<string, InvokeDelegate> _syncMethods;
        private TaskScheduler? _taskScheduler;

        internal NatsServerProxy(NatsConnection parent, string subject, string? queueGroup, INatsSerializer serializer, TContract contract, TaskScheduler? taskScheduler, IReadOnlyDictionary<string, (MethodInfo invoke, MethodInfo serialize)> asyncMethods, IReadOnlyDictionary<string, MethodInfo> syncMethods)
        {
            _parent = parent;
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
            var taskFactory = _taskScheduler != null ? new TaskFactory(_taskScheduler) : null;
            await foreach(var msg in _parent.Subscribe(_subject, _queueGroup, cancellationToken))
            {
                // Perform another rent, the invoke's will release
                msg.Rent();
                try
                {
                    var method = msg.Subject.Substring((_subject ?? string.Empty).Length - 1);
                    if (_asyncMethods.TryGetValue(method, out var delegates))
                    {
                        if (taskFactory == null) await InvokeAsync(delegates.invoke, delegates.serialize, msg, cancellationToken);
#pragma warning disable 4014
                        else taskFactory.StartNew(() => InvokeAsync(delegates.invoke, delegates.serialize, msg, cancellationToken), cancellationToken);
#pragma warning restore 4014
                    }
                    else if (_syncMethods.TryGetValue(method, out var invoke))
                    {
                        if (taskFactory == null) await InvokeSync(invoke, msg, cancellationToken);
#pragma warning disable 4014
                        else taskFactory.StartNew(() => InvokeSync(invoke, msg, cancellationToken), cancellationToken);
#pragma warning restore 4014
                    }
                    else throw new KeyNotFoundException("Unknown method");
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Catch remaining exceptions (shouldn't be any) and pass them 
                    _parent.ServerException(this, msg, ex);
                }
            }
        }

        private async Task InvokeSync(InvokeDelegate invoke, NatsMsg msg, CancellationToken cancellationToken)
        {
            try
            {
                var response = invoke(msg.Payload);
                if (!string.IsNullOrEmpty(msg.ReplyTo))
                    await _parent.PublishAsync(msg.ReplyTo, response, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(msg.ReplyTo))
                {
                    await using var ms = new MemoryStream();
                    var formatter = new BinaryFormatter();
                    formatter.Serialize(ms, ex);

                    await _parent.PublishObjectAsync(msg.ReplyTo, new NatsServerResponse {E = ms.ToArray()}, cancellationToken: cancellationToken);
                }

                _parent.ServerException(this, msg, ex);
            }
            finally
            {
                msg.Release();
            }
        }

        private async Task InvokeAsync(InvokeAsyncDelegate invoke, SerializeDelegate serialize, NatsMsg msg, CancellationToken cancellationToken)
        {
            try
            {
                var task = invoke(msg.Payload);
                await task;
                var response = serialize(task);
                if (!string.IsNullOrEmpty(msg.ReplyTo))
                    await _parent.PublishAsync(msg.ReplyTo, response, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(msg.ReplyTo))
                {
                    await using var ms = new MemoryStream();
                    var formatter = new BinaryFormatter();
                    formatter.Serialize(ms, ex);

                    await _parent.PublishObjectAsync(msg.ReplyTo, new NatsServerResponse { E = ms.ToArray() }, cancellationToken: cancellationToken);
                }

                _parent.ServerException(this, msg, ex);
            }
            finally
            {
                msg.Release();
            }
        }
    }
}
