namespace RequestAsyncNatsSample
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats;

    class Program
    {
        private class Request
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        private class Response
        {
            public int Result { get; set; }
        }

        static async Task Main(string[] args)
        {
            var options = new NatsDefaultOptions
            {
                Echo = true // Without echo this test does not work! On production you might want to keep it disabled
            };

            var connection = new NatsConnection(options);
            connection.ConnectionException += (sender, exception) => Console.WriteLine($"ConnectionException : {exception}");
            connection.StatusChange += (sender, status) => Console.WriteLine($"Connection status changed to {status}");
            connection.ConnectionInformation += (sender, information) => Console.WriteLine($"Connection information {JsonSerializer.Serialize(information)}");
            var cancellation = new CancellationTokenSource();

            var addListenerTask = AddListenerAsync(connection, cancellation.Token);
            var multiplyListenerTask = MultiplyListenerAsync(connection, cancellation.Token);
            var senderTask = SenderAsync(connection, cancellation.Token);
            await connection.ConnectAsync();

            Console.ReadKey();

            cancellation.Cancel();

            try
            {
                await addListenerTask;
            }
            catch (OperationCanceledException)
            { }
            try
            {
                await multiplyListenerTask;
            }
            catch (OperationCanceledException)
            { }
            try
            {
                await senderTask;
            }
            catch (OperationCanceledException)
            { }

            Console.ReadKey();

            await connection.DisposeAsync();

            Console.ReadKey();
        }

        static async Task AddListenerAsync(NatsConnection connection, CancellationToken cancellationToken)
        {
            await using var subscription = await connection.Subscribe<Request>("add");
            await foreach (var request in subscription.WithCancellation(cancellationToken))
                await connection.PublishObjectAsync(request.ReplyTo, new Response {Result = request.Payload.X + request.Payload.Y});
        }

        static async Task MultiplyListenerAsync(NatsConnection connection, CancellationToken cancellationToken)
        {
            await using var subscription = await connection.Subscribe<Request>("multiply");
            await foreach (var request in subscription.WithCancellation(cancellationToken))
                await connection.PublishObjectAsync(request.ReplyTo, new Response { Result = request.Payload.X * request.Payload.Y });
        }

        static async Task SenderAsync(NatsConnection connection, CancellationToken cancellationToken)
        {
            var random = new Random();
            while (!cancellationToken.IsCancellationRequested)
            {
                var request = new Request
                {
                    X = random.Next(1, 100),
                    Y = random.Next(1, 100)
                };

                var add = await connection.RequestObject<Request, Response>("add", request, cancellationToken: cancellationToken);
                Console.WriteLine($"{request.X} + {request.Y} = {add.Result}");

                var multiply = await connection.RequestObject<Request, Response>("multiply", request, cancellationToken: cancellationToken);
                Console.WriteLine($"{request.X} * {request.Y} = {multiply.Result}");

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}
