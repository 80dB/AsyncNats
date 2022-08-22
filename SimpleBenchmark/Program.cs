namespace SimpleBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Runtime;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;

    class Program
    {
        static async Task Main(string[] args)
        {

            var messageSizes = new[] { 0,8, 16, 32, 64, 128, 256, 512, 1024 };

            

            Console.WriteLine();

            //This first test sends a precomputed large buffer of 100mb 
            //It should get us a figure close to what NATS can ingest and drop
            Console.WriteLine("---Publish only ( raw )---");
            foreach (var messageSize in messageSizes)
            {
                await RunBenchmark(1, 0, messageSize, 0,false);
            }
            Console.WriteLine();

            //Here we try to flood NATS but using the normal publish path, generating each message on the fly
            //This gives us an rough idea of the publish path overhead 
            Console.WriteLine("---Publish only ( generate ) 1 pub---");
            foreach (var messageSize in messageSizes)
            {
                await RunBenchmark(1, 0, messageSize, 0);
            }
            Console.WriteLine();

            //Here we repeat the test above but publishing from 100 different tasks
            //This measures the effect of contention
            Console.WriteLine("---Publish only ( generate ) 100 pub---");
            foreach (var messageSize in messageSizes)
            {
                await RunBenchmark(100, 0, messageSize, 0);
            }
            Console.WriteLine();

            
            //Here we try to flood NATS with one processing subscription.
            //It should give us an idea of the read path overhead            
            Console.WriteLine("---Roundtrip raw pub 1 sub---");
            foreach (var messageSize in messageSizes)
            {
                await RunBenchmark(1, 1, messageSize, 0, false);
            }
            Console.WriteLine();

            
            //Here we target a specific message/sec target rate at the writer task
            Console.WriteLine("---Roundtrip 1 pub 1 sub---");
            foreach (var messageSize in messageSizes)
            {
                foreach (var rate in new[] { 100_000, 500_000, 750_000, 1_000_000, 1_250_000})
                {
                    await RunBenchmark(1, 1, messageSize, rate);
                }
                Console.WriteLine();
            }
            Console.WriteLine();

            //Here we add 99 more unrelated subscriptions
            Console.WriteLine("---Roundtrip 1 pub 100 sub---");
            foreach (var messageSize in messageSizes)
            {
                foreach (var rate in new[] { 100_000, 500_000, 750_000, 1_000_000, 1_250_000})
                {
                    await RunBenchmark(1, 100, messageSize, rate);
                }
                Console.WriteLine();
            }
            Console.WriteLine();

            //Here publishing and generating messages from 100 different tasks
            Console.WriteLine("---Roundtrip 100 pub 1 sub---");
            foreach (var messageSize in messageSizes)
            {
                foreach (var rate in new[] { 100_000, 500_000, 750_000, 1_000_000, 1_250_000})
                {
                    await RunBenchmark(100, 1, messageSize, rate);
                }
                Console.WriteLine();
            }
            Console.WriteLine();

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }


        static async Task RunBenchmark(int publishers,int subscribers,int messageSize,int msgPerSecond,bool writerGenerate=true)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

            Console.Write($"Target {(msgPerSecond>0?(msgPerSecond / 1000).ToString()+ "k msg/s" : "flood\t")}\t{messageSize} B\t{publishers} pub\t{subscribers} sub\t:\t");

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

            var subject = Guid.NewGuid().ToString().Split('-')[0];

            if(!writerGenerate)
            {
                foreach (var i in Enumerable.Range(0, publishers))
                {
                    writers.Add(FloodWriterTask(writerConnection, subject, writerCts.Token));
                }
            }
            else
            {
                foreach (var i in Enumerable.Range(0, publishers))
                {
                    writers.Add(WriterTask(writerConnection, subject, (double)msgPerSecond / publishers, writerCts.Token));
                }
            }

            if (subscribers == 1)
            {
                readers.Add(ReaderText(readerConnection, subject, readerCts.Token));
            }
            else if (subscribers > 1)
            {
                foreach (var i in Enumerable.Range(0, subscribers - 1))
                {
                    readers.Add(ReaderText(readerConnection, $"{subject}_{i}", readerCts.Token));
                }
                readers.Add(ReaderText(readerConnection, subject, readerCts.Token));
            }

            double messagesPerSecond = 0;
            double bytesPerSecond = 0;

            double latencyMean = 0;
            double latencyMax = 0;
            double latencyMin = 0;

            if (subscribers > 0)
            {
                
                var sw = Stopwatch.StartNew();
                                
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
                latencyMean = readers.First().Result.sum / Math.Max(1, readers.First().Result.count);
                latencyMax = readers.First().Result.max;
                latencyMin = readers.First().Result.min;

                var ms = Stopwatch.Frequency / 1000d;
                latencyMean = latencyMean / ms;
                latencyMax = latencyMax / ms;
                latencyMin = latencyMin / ms;

                if (!writerGenerate||messageSize==0)
                {
                    latencyMean = 0;
                    latencyMax = 0;
                    latencyMin = 0;
                }

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


            async Task WriterTask(NatsConnection connection, string subject, double msgPerSecond, CancellationToken cancellationToken)
            {
                
                await Task.Yield();
                var message = new byte[messageSize];

                var batch = msgPerSecond >= 500_000 ? 100 : msgPerSecond>=100_000?10:1;
                var delay = msgPerSecond > 0 ? (long)(Stopwatch.Frequency * batch / msgPerSecond) : 0;
                var adjustment = 0L;

                NatsKey subjectUtf8 = new NatsKey(subject);

                while (!cancellationToken.IsCancellationRequested)
                {                    
                    var now = Stopwatch.GetTimestamp();

                    BitConverter.TryWriteBytes(message, now);

                    for(var i = batch-1; i >= 0; i--)
                    {
                        await connection.PublishAsync(subjectUtf8, message, cancellationToken: CancellationToken.None);                        
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

            async Task FloodWriterTask(NatsConnection connection, string subject, CancellationToken cancellationToken)
            {
                await Task.Yield();

               

                var payload = new byte[messageSize];
                var message = NatsPub.RentedSerialize(new NatsMemoryPool(), subject, NatsKey.Empty, payload);

                var bufferSize = 64*1024 * 1024;
                var buffer = new byte[bufferSize].AsMemory(); //hold 1M messages               

                var filled = 0;
                var messageCount = 0;
                while (filled + message.Memory.Length < bufferSize)
                {
                    message.Memory.Span.CopyTo(buffer.Span.Slice(filled));
                    filled += message.Memory.Span.Length;
                    messageCount++;
                }
                buffer = buffer.Slice(0, filled);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await connection.PublishRaw(buffer, messageCount, CancellationToken.None);
                }

            }

            async Task<(long count,long sum,long max,long min)> ReaderText(NatsConnection connection, string subject,CancellationToken cancellationToken)
            {

                await Task.Yield();

                long count = 0;
                long sum = 0;
                long max = 0;
                long min = long.MaxValue;

                try
                {
                    await foreach (var message in connection.SubscribeUnsafe(subject, cancellationToken: cancellationToken))
                    {
                        count++;
                        if (message.Payload.Length > 0)
                        {
                            var timestamp = BitConverter.ToInt64(message.Payload.Span);

                            var now = Stopwatch.GetTimestamp();
                            var rtt = (now - timestamp);
                            sum += rtt;                            
                            max = Math.Max(rtt, max);
                            min = Math.Min(rtt, min);
                        }                        

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