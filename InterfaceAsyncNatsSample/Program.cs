namespace InterfaceAsyncNatsSample
{
    using EightyDecibel.AsyncNats;
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        static async Task Main(string[] args)
        {
            var options = new NatsDefaultOptions
            {
                Serializer = new NatsMessagePackSerializer(),
                Echo = true // Without echo this test does not work! On production you might want to keep it disabled
            };

            var connection = new NatsConnection(options);
            connection.ConnectionException += (sender, exception) => Console.WriteLine($"ConnectionException : {exception}");
            connection.StatusChange += (sender, status) => Console.WriteLine($"Connection status changed to {status}");
            connection.ConnectionInformation += (sender, information) => Console.WriteLine($"Connection information {JsonSerializer.Serialize(information)}");
            var cancellation = new CancellationTokenSource();

            var listenerTask = connection.StartContractServer<IContract>(new Server(), cancellation.Token, "IContract");
            await connection.ConnectAsync();

            var client = connection.GenerateContractClient<IContract>("IContract");
            var result = await client.MultiplyAsync(10, 10);
            Console.WriteLine("Multiply Result: {0}", result);
            
            result = client.Add(10, 10);
            Console.WriteLine("Add Result: {0}", result);

            result = await client.RandomAsync();
            Console.WriteLine("RandomAsync Result: {0}", result);

            result = client.Random();
            Console.WriteLine("Random Result: {0}", result);

            await client.SayAsync("Hello Async World");
            client.Say("Hello Sync World");

            try
            {
                client.ThrowException();
            }
            catch(Exception ex)
            {
                Console.WriteLine("Exception: {0}\n{1}", ex.Message, ex.StackTrace);
            }

            try
            {
                await client.ThrowExceptionOnMethodWithReturn();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: {0}\n{1}", ex.Message, ex.StackTrace);
            }

            Console.ReadKey();

            cancellation.Cancel();

            try
            {
                await listenerTask;
            }
            catch (OperationCanceledException)
            {
            }

            await connection.DisposeAsync();
        }
    }
}