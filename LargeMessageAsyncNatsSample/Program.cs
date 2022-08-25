namespace LargeMessageAsyncNatsSample
{
    using EightyDecibel.AsyncNats;
    using System;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
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
            await connection.ConnectAsync();

            var cancellation = new CancellationTokenSource();
            var listener = ListenerTask(connection, cancellation.Token);
            var sender = SenderTask(connection, cancellation.Token);

            await sender;
            await listener;
        }

        static async Task ListenerTask(NatsConnection connection, CancellationToken cancellationToken)
        {
            await foreach (var text in connection.SubscribeText("long.message", cancellationToken: cancellationToken))
            {
                Console.WriteLine("Received string {0} characters long", text.Length);
                break;
            }
        }

        static async Task SenderTask(NatsConnection connection, CancellationToken cancellationToken)
        {
            // Not very efficient, but it's the idea ;)
            var sb = new StringBuilder();
            while (sb.Length < 900_000)
            {
                sb.Append((sb.Length % 10).ToString());
            }

            var str = sb.ToString();
            Console.WriteLine("Sending string {0} characters long", str.Length);
            await connection.PublishAsync("long.message", str, cancellationToken: cancellationToken);
        }
    }
}
