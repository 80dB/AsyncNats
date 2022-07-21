using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EightyDecibel.AsyncNats.Rpc
{
    using System;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading.Tasks;

    public abstract class NatsClientProxy
    {
        private readonly INatsConnection _connection;
        private readonly string _baseSubject;
        private readonly ILogger? _logger;

        protected NatsClientProxy(INatsConnection connection, string baseSubject, ILogger? logger)
        {
            _connection = connection;
            _baseSubject = baseSubject;
            _logger = logger;
        }

        protected async Task<TResult> InvokeAsync<TParameters, TResult>(string method, TParameters parameters)
        {
            var subject = $"{_baseSubject}.{method}";
            
            _logger?.LogTrace("Sending request to {Subject} with {Parameters}", subject, JsonSerializer.Serialize(parameters));

            var response = await _connection.RequestObject<TParameters, NatsServerResponse<TResult>>(subject, parameters);
            if (response.E == null)
            {
                _logger?.LogTrace("Received response from {Subject} with {Response}", subject, response.R);
                return response.R;
            }

            using var ms = new MemoryStream(response.E);
            var formatter = new BinaryFormatter();
            var exception = (Exception) formatter.Deserialize(ms);

            _logger?.LogTrace(exception, "Received exception from {Subject} with {Exception}", subject, exception?.Message);

            throw exception!;
        }

        protected TResult Invoke<TParameters, TResult>(string method, TParameters parameters)
        {
            var task = InvokeAsync<TParameters, TResult>(method, parameters);
            task.ConfigureAwait(false);
            task.Wait();
            return task.Result;
        }

        protected void InvokeVoid<TParameters>(string method, TParameters parameters)
        {
            var task = InvokeAsync<TParameters, object>(method, parameters);
            task.ConfigureAwait(false);
            task.Wait();
        }

        protected Task PublishAsync<TParameters>(string method, TParameters parameters)
        {
            var subject = $"{_baseSubject}.{method}";
            _logger?.LogTrace("Publishing to {Subject} with {Parameters}", subject, JsonSerializer.Serialize(parameters));
            return _connection.PublishObjectAsync(subject, parameters).AsTask();
        }

        protected void Publish<TParameters>(string method, TParameters parameters)
        {
            var task = PublishAsync(method, parameters);
            task.ConfigureAwait(false);
            task.Wait();
        }
    }
}
