namespace SimpleBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
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
                await RunBenchmark(1, 0, messageSize);
                await RunBenchmark(1, 1, messageSize);
                await RunBenchmark(2, 1, messageSize);
                await RunBenchmark(2, 2, messageSize);                
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }


        static async Task RunBenchmark(int publishers,int subscribers,int messageSize)
        {

            Console.Write($"{messageSize} B\t{publishers} pub\t{subscribers} sub\t:\t");

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
            List<Task> readers = new List<Task>();

            foreach(var i in Enumerable.Range(0, publishers))
            {
                var task = WriterTask(writerConnection, writerCts.Token);
                writers.Add(task);
            }

            foreach (var i in Enumerable.Range(0, subscribers))
            {
                var task = ReaderText(readerConnection, readerCts.Token);
                readers.Add(task);
            }

            double messagesPerSecond = 0;
            double bytesPerSecond = 0;

            if (subscribers > 0)
            {
                //wait 10M messages
                var sw = Stopwatch.StartNew();

                while (writerConnection.TransmitMessagesTotal < 10_000_000)
                    await Task.Delay(250);

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
            }
            else
            {
                //get writer only

                //wait 10M messages
                var sw = Stopwatch.StartNew();

                while (writerConnection.TransmitMessagesTotal < 10_000_000)
                    await Task.Delay(250);

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

            Console.Write($"{messagesPerSecond / 1000:f2}k msg/s\t{bytesPerSecond / 1000_0000:f2} MB/s");
            Console.Write("\r\n");

            await Task.WhenAll(writers);
            await Task.WhenAll(readers);

            await readerConnection.DisposeAsync();
            await writerConnection.DisposeAsync();


            async Task WriterTask(NatsConnection connection, CancellationToken cancellationToken)
            {
                await Task.Yield();
                var message = new byte[messageSize];

                while (!cancellationToken.IsCancellationRequested)
                {
                    await connection.PublishMemoryAsync("benchmark", message, cancellationToken: CancellationToken.None);
                }
            }

            async Task ReaderText(NatsConnection connection, CancellationToken cancellationToken)
            {
                await Task.Yield();
                try
                {
                    await foreach (var message in connection.SubscribeUnsafe("benchmark", cancellationToken: cancellationToken))
                    {
                        //just drop
                        message.Release();
                    }
                }
                catch(OperationCanceledException ex)
                {
                    //swallow
                }
                

            }
        }
    }
}