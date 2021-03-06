﻿namespace InterfaceAsyncNatsSample
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
                Echo = true, // Without echo this test does not work! On production you might want to keep it disabled
                RequestTimeout = TimeSpan.FromSeconds(2),
            };

            var connection = new NatsConnection(options);
            connection.ConnectionException += (sender, exception) => Console.WriteLine($"ConnectionException : {exception.Message}");
            connection.StatusChange += (sender, status) => Console.WriteLine($"Connection status changed to {status}");
            connection.ConnectionInformation += (sender, information) => Console.WriteLine($"Connection information {JsonSerializer.Serialize(information)}");
            var cancellation = new CancellationTokenSource();

            var listenerTask = connection.StartContractServer<IContract>(new Server(), cancellation.Token, "IContract");
            await connection.ConnectAsync();

            var count = 0;
            while (connection.Status != NatsStatus.Connected)
            {
                await Task.Delay(50, cancellation.Token);
                count++;
                if (count <= 100) continue;

                Console.WriteLine("Could not connect to nats server");
                await connection.DisposeAsync();
                return;
            }

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

            await client.FireAndForget(1, 2, 3);
            Console.WriteLine("After FireAndForget - Note that ThrowException will not execute until after FireAndForget finishes (server is single threaded)");

            Console.WriteLine("Request timeout test");
            try
            {
                await client.Timeout();
                Console.WriteLine("ERROR: The Timeout call should have timed out!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Expected exception: {0}", ex.InnerException?.Message ?? ex.Message);
            }

            try
            {
                client.ThrowException();
            }
            catch(Exception ex)
            {
                Console.WriteLine("Expected exception: {0}", ex.Message);
            }

            try
            {
                await client.ThrowExceptionOnMethodWithReturn();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Expected exception: {0}", ex.Message);
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