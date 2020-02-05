namespace InterfaceAsyncNatsSample
{
    using System.Threading.Tasks;

    public interface IContract
    {
        Task<int> MultiplyAsync(int x, int y);

        int Add(int x, int y);

        Task<int> RandomAsync();

        int Random();

        Task SayAsync(string text);

        void Say(string text);

        void ThrowException();

        Task<int> ThrowExceptionOnMethodWithReturn();
    }
}
