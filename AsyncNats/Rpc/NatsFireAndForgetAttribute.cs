namespace EightyDecibel.AsyncNats.Rpc
{
    using System;

    /// <summary>
    ///    Do not wait for the server to respond
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class NatsFireAndForgetAttribute : Attribute
    { }
}
