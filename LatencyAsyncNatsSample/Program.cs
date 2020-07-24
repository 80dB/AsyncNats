namespace LatencyAsyncNatsSample
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO.Pipelines;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;

    class Program
    {
        private static long _started = 0;
        private static long _ended = 0;
        private static List<long> _timings = new List<long>();

        static async Task Main(string[] args)
        {
            var options = new NatsDefaultOptions
            {
                SenderPipeOptions = new PipeOptions(pauseWriterThreshold: 1024*1024*10, resumeWriterThreshold: 1024*1024, useSynchronizationContext: false),
                SenderQueueLength = 5000,
                ReceiverPipeOptions = new PipeOptions(pauseWriterThreshold: 1024*1024*10, resumeWriterThreshold: 1024*1024, useSynchronizationContext: false),
                ReceiverQueueLength = 500,
                FlushAtLeastEvery = 1
            };

            await using var readerConnection = new NatsConnection(options);
            readerConnection.ConnectionException += (sender, exception) => Console.WriteLine($"ConnectionException : {exception}");
            readerConnection.StatusChange += (sender, status) => Console.WriteLine($"Connection status changed to {status}");
            readerConnection.ConnectionInformation += (sender, information) => Console.WriteLine($"Connection information {JsonSerializer.Serialize(information)}");

            
            var cancellation = new CancellationTokenSource();
            await readerConnection.ConnectAsync();

            var count = 0;
            while (readerConnection.Status != NatsStatus.Connected)
            {
                await Task.Delay(50, cancellation.Token);
                count++;
                if (count > 100)
                {
                    Console.WriteLine("Could not connect to nats server");
                    await readerConnection.DisposeAsync();
                    return;
                }
            }


            var tasks = new[]
            {
                Task.Run(() => Reader(readerConnection, cancellation.Token), cancellation.Token)
            }.Concat(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Writer(options, cancellation.Token), cancellation.Token))).ToArray();


            var frequency = (double) Stopwatch.Frequency;
            while (!Console.KeyAvailable)
            {
                Console.Write($"\rMessages processed {_timings.Count} ({_timings.Count / ((Stopwatch.GetTimestamp() - _started) / frequency):N2} messages/sec)  {readerConnection.ReceiverQueueSize}           ");
                await Task.Delay(1000, cancellation.Token);
            }

            cancellation.Cancel();

            try
            {
                Task.WaitAll(tasks);
            }
            catch 
            { }


            Console.WriteLine($"Measured {_timings.Count} messages in {(_ended - _started) / frequency:N2} seconds ({_timings.Count / ((_ended - _started) / frequency):N2} messages/sec)");

            _timings.Sort();

            var ms = Stopwatch.Frequency / 1000d;
            Console.WriteLine("Latencies:");
            Console.WriteLine($"Slowest: {_timings[^1] / ms:N2}ms");
            Console.WriteLine($"Fastest: {_timings[0] / ms:N2}ms");
            Console.WriteLine($"Average: {_timings.Average() / ms:N2}ms");

            Console.WriteLine("Latencies quantiles:");
            Console.WriteLine($"10: {_timings[(int)(_timings.Count * 0.1)] / ms:N2}ms");
            Console.WriteLine($"50: {_timings[(int)(_timings.Count * 0.5)] / ms:N2}ms");
            Console.WriteLine($"75: {_timings[(int)(_timings.Count * 0.75)] / ms:N2}ms");
            Console.WriteLine($"90: {_timings[(int)(_timings.Count * 0.9)] / ms:N2}ms");
            Console.WriteLine($"99: {_timings[(int)(_timings.Count * 0.99)] / ms:N2}ms");
            Console.WriteLine($"99.9: {_timings[(int)(_timings.Count * 0.999)] / ms:N2}ms");
            Console.WriteLine($"99.99: {_timings[(int)(_timings.Count * 0.9999)] / ms:N2}ms");
            Console.WriteLine($"99.999: {_timings[(int)(_timings.Count * 0.99999)] / ms:N2}ms");
            Console.WriteLine($"99.9999: {_timings[(int)(_timings.Count * 0.999999)] / ms:N2}ms");
            Console.WriteLine($"99.99999: {_timings[(int)(_timings.Count * 0.9999999)] / ms:N2}ms");
        }

        static async Task Reader(NatsConnection connection, CancellationToken cancellationToken)
        {
            await foreach (var msg in connection.Subscribe("latency-test", cancellationToken: cancellationToken))
            {
                var timestamp = BitConverter.ToInt64(msg.Payload.Span);
                _timings.Add(Stopwatch.GetTimestamp() - timestamp);
            }
        }

        static async Task Writer(INatsOptions options, CancellationToken cancellationToken)
        {
            await using var connection = new NatsConnection(options);
            connection.StatusChange += (sender, status) => Console.WriteLine($"Connection status changed to {status}");
            await connection.ConnectAsync();

            while (connection.Status != NatsStatus.Connected)
            {
                await Task.Delay(50, cancellationToken);
            }

            _started = Stopwatch.GetTimestamp();
            try
            {
                var buffer = new byte[1024];
                while (!cancellationToken.IsCancellationRequested)
                {
                    BitConverter.TryWriteBytes(buffer.AsSpan(), Stopwatch.GetTimestamp());
                    await connection.PublishAsync("latency-test", buffer, cancellationToken: cancellationToken);

                    while (connection.SenderQueueSize > 1024*5)
                        Thread.Sleep(1);
                }
            }
            finally
            {
                _ended = Stopwatch.GetTimestamp();
            }
        }
    }
}
