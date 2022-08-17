namespace SimpleBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats;

    class Program
    {
        static async Task Main(string[] args)
        {

            var messageSizes = new[] { 8, 16, 32, 64, 128, 256, 512, 1024 };

            foreach(var messageSize in messageSizes)
            {
                foreach(var rate in new[] { 10_000, 100_000, 500_000,750_000 })
                {
                    await RunBenchmark(1, 0, messageSize, rate);
                    await RunBenchmark(1, 1, messageSize, rate);
                    await RunBenchmark(2, 1, messageSize, rate);
                }
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }


        static async Task RunBenchmark(int publishers,int subscribers,int messageSize,int msgPerSecond)
        {

            Console.Write($"Target {(msgPerSecond>0?(msgPerSecond / 1000).ToString()+ "k msg/s" : "flood")}\t{messageSize} B\t{publishers} pub\t{subscribers} sub\t:\t");

            var options = new NatsDefaultOptions();

            var writerConnection = new NatsConnection(options);           
            var readerConnection = new NatsConnection(options);
            
            var writerCts = new CancellationTokenSource();
            var readerCts = new CancellationTokenSource();

            await writerConnection.ConnectAsync();
            await readerConnection.ConnectAsync();

            var count = 0;
            while (writerConnection.Status != NatsStatus.Connected)
            {
                await Task.Delay(50);
                count++;
                if (count > 100)
                {
                    Console.WriteLine("Could not connect to nats server");
                    await writerConnection.DisposeAsync();
                    await readerConnection.DisposeAsync();
                    return;
                }
            }
            while (readerConnection.Status != NatsStatus.Connected)
            {
                await Task.Delay(50);
                count++;
                if (count > 100)
                {
                    Console.WriteLine("Could not connect to nats server");
                    await writerConnection.DisposeAsync();
                    await readerConnection.DisposeAsync();
                    return;
                }
            }


            List<Task> writers = new List<Task>();
            List<Task<(long count, long sum, long max, long min)>> readers = new List<Task<(long count, long sum, long max, long min)>>();
            foreach(var i in Enumerable.Range(0, publishers))
            {
                var task = WriterTask(writerConnection, (double)msgPerSecond/ publishers, writerCts.Token);
                writers.Add(task);
            }

            foreach (var i in Enumerable.Range(0, subscribers))
            {
                var task = ReaderText(readerConnection, readerCts.Token);
                readers.Add(task);
            }

            double messagesPerSecond = 0;
            double bytesPerSecond = 0;

            double latencyMean = 0;
            double latencyMax = 0;
            double latencyMin = 0;

            if (subscribers > 0)
            {
                
                var sw = Stopwatch.StartNew();

                //wait 10M messages
                //while (writerConnection.TransmitMessagesTotal < 10_000_000)
                //    await Task.Delay(250);

                await Task.Delay(TimeSpan.FromSeconds(10));

                writerCts.Cancel(); //stop writing

                //wait complete flush
                while (writerConnection.SenderQueueSize > 0)
                    await Task.Delay(50);

                while (readerConnection.ReceiverQueueSize > 0)
                    await Task.Delay(50);

                readerCts.Cancel(); //stop reading

                sw.Stop();

                var totalMessages = readerConnection.ReceivedMessagesTotal;
                var totalBytes = readerConnection.ReceivedBytesTotal;
                var totalSecondsElapsed = sw.Elapsed.TotalSeconds;

                messagesPerSecond = totalMessages / totalSecondsElapsed;
                bytesPerSecond= totalBytes / totalSecondsElapsed;

                await Task.WhenAll(readers);
                latencyMean = readers.Select(r => r.Result.sum).Sum() / readers.Select(r => r.Result.count).Sum();
                latencyMax = readers.Select(r => r.Result.max).Max();
                latencyMin = readers.Select(r => r.Result.min).Max();

                var ms = Stopwatch.Frequency / 1000d;
                latencyMean = latencyMean / ms;
                latencyMax = latencyMax / ms;
                latencyMin = latencyMin / ms;

            }
            else
            {
                //get writer only

                var sw = Stopwatch.StartNew();

                await Task.Delay(TimeSpan.FromSeconds(10));

                //while (writerConnection.TransmitMessagesTotal < 10_000_000)
                //    await Task.Delay(250);

                writerCts.Cancel();

                while (writerConnection.SenderQueueSize > 0)
                    await Task.Delay(50);

                sw.Stop();

                var totalMessages = writerConnection.TransmitMessagesTotal;
                var totalBytes= writerConnection.TransmitBytesTotal;
                var totalSecondsElapsed = sw.Elapsed.TotalSeconds;

                messagesPerSecond = totalMessages / totalSecondsElapsed;
                bytesPerSecond = totalBytes / totalSecondsElapsed;
            }

            Console.Write($"{messagesPerSecond / 1000:f1}k msg/s\t{bytesPerSecond / 1000_0000:f2} MB/s Latency: {latencyMean:f2}ms {latencyMax:f2}ms {latencyMin:f2}ms");
            Console.Write("\r\n");

            await Task.WhenAll(writers);
            await Task.WhenAll(readers);

            await readerConnection.DisposeAsync();
            await writerConnection.DisposeAsync();


            async Task WriterTask(NatsConnection connection, double msgPerSecond, CancellationToken cancellationToken)
            {
                await Task.Yield();
                var message = new byte[messageSize];

                var batch = msgPerSecond >= 500_000 ? 100 : msgPerSecond>=100_000?10:1;
                var delay = msgPerSecond > 0 ? (long)(Stopwatch.Frequency * batch / msgPerSecond) : 0;

                var adjustment = 0L;

                while (!cancellationToken.IsCancellationRequested)
                {                    
                    var now = Stopwatch.GetTimestamp();

                    BitConverter.TryWriteBytes(message, now);

                    for(var i = batch-1; i >= 0; i--)
                    {
                        await connection.PublishMemoryAsync("benchmark", message, cancellationToken: CancellationToken.None);                        
                    }

                    if (delay ==0) continue;

                    var elapsed = Stopwatch.GetTimestamp() - now;

                    var target = delay + adjustment;
                    while (elapsed< target)
                    {
                        await Task.Yield();
                        elapsed = Stopwatch.GetTimestamp() - now;                    
                    }
                    adjustment = (long)((delay-elapsed));
                }
            }

            async Task<(long count,long sum,long max,long min)> ReaderText(NatsConnection connection, CancellationToken cancellationToken)
            {

                await Task.Yield();

                long count = 0;
                long sum = 0;
                long max = 0;
                long min = long.MaxValue;

                try
                {
                    await foreach (var message in connection.SubscribeUnsafe("benchmark", cancellationToken: cancellationToken))
                    {

                        var timestamp=BitConverter.ToInt64(message.Payload.Span);

                        var now = Stopwatch.GetTimestamp();
                        var rtt= (now - timestamp);
                        sum += rtt;
                        count++;
                        max = Math.Max(rtt, max);
                        min = Math.Min(rtt, min);

                        message.Release();
                    }
                }
                catch(OperationCanceledException ex)
                {
                    //swallow
                }

                return (count,sum, max, min);

            }
        }
    }
}