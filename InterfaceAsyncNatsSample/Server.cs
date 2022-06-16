namespace InterfaceAsyncNatsSample
{
    using System;
    using System.Threading.Tasks;

    public class Server : IContract
    {
        private Random _random = new Random();

        public Task<int> MultiplyAsync(int x, int y)
        {
            return Task.FromResult(x*y);
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
            throw new Exception("Server exception in ThrowException");
        }

        public Task<int> ThrowExceptionOnMethodWithReturn()
        {
            throw new Exception("Server exception in ThrowExceptionOnMethodWithReturn");
        }

        public async Task Timeout()
        {
            Console.WriteLine("Waiting 3 seconds, 1 second longer than timeout");
            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        public async Task FireAndForget(int x, int y, int z)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            Console.WriteLine("FireAndForget: {0},{1},{2}", x, y, z);
        }
    }
}
