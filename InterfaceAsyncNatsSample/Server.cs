namespace InterfaceAsyncNatsSample
{
    using System;
    using System.Threading.Tasks;

    public class Server : IContract
    {
        private Random _random = new Random();

        public async Task<int> MultiplyAsync(int x, int y)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            return x * y;
        }

        public int Add(int x, int y)
        {
            return x + y;
        }

        public Task<int> RandomAsync()
        {
            return Task.FromResult(_random.Next());
        }

        public int Random()
        {
            return _random.Next();
        }

        public Task SayAsync(string text)
        {
            Console.WriteLine("SayAsync: {0}", text);
            return Task.CompletedTask;
        }

        public void Say(string text)
        {
            Console.WriteLine("Say: {0}", text);
        }

        public void ThrowException()
        {
            throw new Exception("Server exception");
        }
    }
}
