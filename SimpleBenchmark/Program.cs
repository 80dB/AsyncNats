namespace SimpleBenchmark
{
    using System;
    using System.Buffers;
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

            //args = new[] { "writer","32", "benchmark" };
            if (args.Length>0 && args[0] == "writer")
            {
                var messageSize = 32;
                if (args.Length > 1)
                    int.TryParse(args[1], out messageSize);

                string subject = null;
                if (args.Length > 2)
                    subject = args[2];

                var exit = false;
                _ = Task.Run(async () => {
                    while (!exit)
                    {
                        await RunBenchmark(1, 0, messageSize, 0, false,subject: subject);
                    }
                   
                });

                Console.ReadKey();
                exit = true;
                return;
            }

            //args = new[] { "reader","benchmark" };
            if (args.Length > 0 && args[0] == "reader")
            {
                string subject = null;
                if (args.Length > 1)
                    subject = args[1];

                var exit = false;
                _ = Task.Run(async () => {
                    while (!exit)
                    {
                        await RunBenchmark(0, 1, 0, 0, false, subject: subject);
                    }

                });

                Console.ReadKey();
                exit = true;
                return;
            }

            Console.WriteLine();

            //goto skip;
        
            //This first test sends a precomputed large buffer of 100mb 
            //It should get us a figure close to what NATS can ingest and drop
            Console.WriteLine("---Publish only ( raw )---");
            foreach (var messageSize in messageSizes)
            {
                await RunBenchmark(4, 0, messageSize, 0,false);
            }
            Console.WriteLine();

        //skip:
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
                await RunBenchmark(4, 1, messageSize, 0,writerGenerate: false, readerType:ReaderType.Normal);
                await RunBenchmark(4, 1, messageSize, 0, writerGenerate: false, readerType: ReaderType.Unsafe);
                await RunBenchmark(4, 1, messageSize, 0, writerGenerate: false, readerType: ReaderType.Inline);
                Console.WriteLine();
            }
            Console.WriteLine();

        
            //Here we target a specific message/sec target rate at the writer task
            messageSizes = new[] { 8, 32, 128, 512, 1024 };
            Console.WriteLine("---Roundtrip 1 pub 1 sub---");
            foreach (var messageSize in messageSizes)
            {
                foreach (var rate in new[] { 1_000, 10_000, 100_000, 500_000, 750_000, 1_000_000, 1_250_000, 1_500_000, 2_000_000, 2_500_000, 3_000_000 })
                {
                    await RunBenchmark(1, 1, messageSize, rate);
                }
                Console.WriteLine();
            }
            Console.WriteLine();
            

            //Here we add 99 more unrelated subscriptions
            Console.WriteLine("---Roundtrip 1 pub 100 sub---");
            messageSizes = new[] { 8, 32, 128, 512, 1024 };
            foreach (var messageSize in messageSizes)
            {
                foreach (var rate in new[] { 1_000, 10_000,100_000, 500_000, 750_000, 1_000_000, 1_250_000})
                {
                    await RunBenchmark(1, 100, messageSize, rate);
                }
                Console.WriteLine();
            }
            Console.WriteLine();


        
            messageSizes = new[] { 8, 16, 32, 64, 128, 256, 512, 1024 };

            //Here publishing and generating messages from 100 different tasks
            Console.WriteLine("---Roundtrip 100 pub 1 sub---");
            foreach (var messageSize in messageSizes)
            {
                foreach (var rate in new[] { 1000, 10_000,50_000, 100_000, 500_000, 750_000, 1_000_000, 1_250_000, 1_500_000, 2_000_000 })
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

        static async Task RunBenchmark(int publishers, int subscribers, int messageSize, int msgPerSecond, bool writerGenerate = true, ReaderType readerType = ReaderType.Inline, TimeSpan? duration = null, string subject = null)
        {
            try
            {
                await RunBenchmarkInner(publishers, subscribers, messageSize, msgPerSecond, writerGenerate, readerType);        
            }
            catch(Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }
        }
        static async Task RunBenchmarkInner(int publishers,int subscribers,int messageSize,int msgPerSecond,bool writerGenerate=true,ReaderType readerType= ReaderType.Inline, TimeSpan? duration=null,string subject=null)
        {
            duration = duration ?? TimeSpan.FromSeconds(15);
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

            Console.Write($"Target {(msgPerSecond>0?(msgPerSecond / 1000).ToString("0000")+ "k msg/s" : "flood\t")} ({readerType})\t{messageSize} B\t{publishers} pub\t{subscribers} sub : ");

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
            List<Task<(long count, long sum, long max, long min, double mean)>> readers = new List<Task<(long count, long sum, long max, long min,double mean)>>();

            subject = subject?? Guid.NewGuid().ToString().Split('-')[0];

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
                readers.Add(ReaderText(readerConnection, subject, readerType, readerCts.Token));
            }
            else if (subscribers > 1)
            {
                readers.Add(ReaderText(readerConnection, subject, readerType, readerCts.Token));

                foreach (var i in Enumerable.Range(0, subscribers - 1))
                {
                    readers.Add(ReaderText(readerConnection, $"{subject}_{i}", readerType, readerCts.Token));
                }
            }

            double messagesPerSecond = 0;
            double bytesPerSecond = 0;

            double latencyMean = 0;
            double latencyMax = 0;
            double latencyMin = 0;

            if (subscribers > 0)
            {
                
                var sw = Stopwatch.StartNew();
                                
                await Task.Delay(duration.Value);

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
                latencyMean = (double)readers.First().Result.mean;
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

            Console.Write($"{messagesPerSecond / 1000:f1}k msg/s\t{bytesPerSecond / 1000_0000:f2} MB/s Lat: {latencyMean:f2}ms {latencyMax:f2}ms {latencyMin:f2}ms");
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

                var targetPerMessage = delay / batch;
                
                var messageCount = 0;

                var initialRandomDelay = Random.Shared.Next(0, 10);

                await Task.Delay(initialRandomDelay);

                NatsKey subjectUtf8 = new NatsKey(subject);

                var start = Stopwatch.GetTimestamp();

                while (!cancellationToken.IsCancellationRequested)
                {
                    var now = Stopwatch.GetTimestamp();
                    for (var i = batch - 1; i >= 0; i--)
                    {
                        BitConverter.TryWriteBytes(message, Stopwatch.GetTimestamp());
                        await connection.PublishAsync(subjectUtf8, message, cancellationToken: CancellationToken.None);
                    }

                    messageCount += batch;

                    if (delay ==0) continue;

                    var elapsed = Stopwatch.GetTimestamp() - now;

                    var perMessage = (Stopwatch.GetTimestamp() - start) / messageCount;

                    while(perMessage < targetPerMessage)
                    {
                        var wait = (targetPerMessage - perMessage) * messageCount;
                        var waitMs = (double)wait *1000/ Stopwatch.Frequency;

                        if (waitMs >= 1)
                            await Task.Delay((int)waitMs);
                        else
                            break; //let speed up

                        perMessage = (Stopwatch.GetTimestamp() - start) / messageCount;
                    }
                }
            }

            async Task FloodWriterTask(NatsConnection connection, string subject, CancellationToken cancellationToken)
            {
                await Task.Yield();

                var payload = new byte[messageSize];
                var message = NatsPub.Serialize(subject, NatsKey.Empty, payload);

                var bufferSize = 1024 * 64;
                var byteArray = new byte[bufferSize];
                var memory = byteArray.AsMemory(); //hold 1M messages               

                var filled = 0;
                var messageCount = 0;
                while (filled + message.Length < bufferSize)
                {
                    message.Span.CopyTo(memory.Span.Slice(filled));
                    filled += message.Span.Length;
                    messageCount++;
                }
                memory = memory.Slice(0, filled);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await connection.PublishRaw(byteArray,filled, messageCount, CancellationToken.None);
                }

            }

            async Task<(long count,long sum,long max,long min,double mean)> ReaderText(NatsConnection connection, string subject, ReaderType readerType,CancellationToken cancellationToken)
            {

                await Task.Yield();

                long count = 0;
                long sum = 0;
                long max = 0;
                long min = long.MaxValue;
                double mean = 0.0;

                try
                {
                    void Process(ref NatsInlineMsg message)
                    {
                        count++;
                        var payload = message.Payload;

                        if (payload.Length > 0)
                        {
                            long timestamp = 0;
                            if (payload.IsSingleSegment || payload.FirstSpan.Length>=8)
                            {
                                timestamp = BitConverter.ToInt64(payload.FirstSpan);
                            }
                            else
                            {
                                Span<byte> timestampSpan = stackalloc byte[8];
                                payload.Slice(0, 8).CopyTo(timestampSpan);
                                timestamp = BitConverter.ToInt64(timestampSpan);
                            }

                            var now = Stopwatch.GetTimestamp();
                            var rtt = (now - timestamp);

                            mean += (rtt - mean) / (double)count;

                            sum += rtt;
                            max = Math.Max(rtt, max);
                            min = Math.Min(rtt, min);
                        }                        
                    }

                    if(readerType==ReaderType.Inline)
                    {
                        await connection.SubscribeUnsafe(subject, Process, cancellationToken: cancellationToken);
                    }
                    else if(readerType ==ReaderType.Unsafe)
                    {
                        await foreach (var message in connection.SubscribeUnsafe(subject, cancellationToken: cancellationToken))
                        {
                            count++;
                            var payload = message.Payload;
                            if (payload.Length > 0)
                            {
                                var timestamp = BitConverter.ToInt64(payload.Span);

                                var now = Stopwatch.GetTimestamp();
                                var rtt = (now - timestamp);

                                mean += (rtt - mean) / (double)count;

                                sum += rtt;
                                max = Math.Max(rtt, max);
                                min = Math.Min(rtt, min);
                            }
                        }
                    }
                    else
                    {
                        await foreach (var message in connection.Subscribe(subject, cancellationToken: cancellationToken))
                        {
                            count++;
                            var payload = message.Payload;
                            if (payload.Length > 0)
                            {
                                var timestamp = BitConverter.ToInt64(payload.Span);

                                var now = Stopwatch.GetTimestamp();
                                var rtt = (now - timestamp);

                                mean += (rtt - mean) / (double)count;

                                sum += rtt;
                                max = Math.Max(rtt, max);
                                min = Math.Min(rtt, min);
                            }
                        }
                    }

                }
                catch(OperationCanceledException)
                {
                    //swallow
                }

                return (count,sum, max, min,mean);

            }
        }
    }

    enum ReaderType
    {
        Normal,
        Unsafe,
        Inline
    }
}