namespace EightyDecibel.AsyncNats.Messages
{
    using System;
    using System.Text;

    public class NatsPing : INatsClientMessage,INatsServerMessage
    {
        private static readonly ReadOnlyMemory<byte> _command = Encoding.UTF8.GetBytes("PING\r\n");

        public static readonly NatsPing Instance = new NatsPing();

        public int Length => _command.Length;

       

        public void Serialize(Span<byte> buffer)
        {
            _command.Span.CopyTo(buffer);
        }

        public static ReadOnlyMemory<byte> Serialize()
        {
            return _command;
        }
    }
}