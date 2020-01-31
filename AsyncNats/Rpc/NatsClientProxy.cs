namespace EightyDecibel.AsyncNats.Rpc
{
    using System;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading.Tasks;

    public abstract class NatsClientProxy
    {
        private INatsConnection _connection;
        private string _baseSubject;

        protected NatsClientProxy(INatsConnection connection, string baseSubject)
        {
            _connection = connection;
            _baseSubject = baseSubject;
        }

        protected async Task<TResult> InvokeAsync<TParameters, TResult>(string method, TParameters parameters)
        {
            var response = await _connection.RequestObject<TParameters, NatsServerResponse<TResult>>($"{_baseSubject}.{method}", parameters);
            if (response.E == null) return response.R;

            using var ms = new MemoryStream(response.E);
            var formatter = new BinaryFormatter();
            throw (Exception)formatter.Deserialize(ms);
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
            return;
        }
    }
}
