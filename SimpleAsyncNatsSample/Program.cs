namespace SimpleAsyncNatsSample
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats;

    class Program
    {
        static async Task Main(string[] args)
        {
            var options = new NatsOptions();
            options.Serializer = new NatsAsciiSerializer();
            var connection = new NatsConnection(options);
            var cancellation = new CancellationTokenSource();

            var readerTypedTask = ReaderTyped(connection, cancellation.Token);
            var writerTask = Writer(connection, cancellation.Token);
            await connection.ConnectAsync();

            Console.ReadKey();

            cancellation.Cancel();
            await readerTypedTask;
            await writerTask;

            Console.ReadKey();

            await connection.DisconnectAsync();
        }

        static async Task ReaderTyped(NatsConnection connection, CancellationToken cancellationToken)
        {
            var history = new Queue<(int count, long time)>();
            var counter = 0;
            var prev = 0;
            await using var messages = await connection.Subscribe<string>("HELLO");
            var watch = Stopwatch.StartNew();
            await foreach (var message in messages.WithCancellation(cancellationToken))
            {
                counter++;
                if (counter % 1_000_000 != 0) continue;
                watch.Stop();
                history.Enqueue((counter - prev, watch.ElapsedMilliseconds));
                prev = counter;
                if (history.Count > 10) history.Dequeue();

                var count = history.Sum(h => h.count);
                var time = history.Sum(h => h.time);

                Console.WriteLine($"{message.GetType()} - {message.Payload} - {count / (double)time * 1000}");
                watch = Stopwatch.StartNew();
            }
        }

        static async Task Writer(NatsConnection connection, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await connection.PublishAsync("HELLO", payload: "HELLO WORLD");
            }
        }
    }
}
