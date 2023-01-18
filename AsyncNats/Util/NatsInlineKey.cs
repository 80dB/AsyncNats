namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text;

    public readonly ref struct NatsInlineKey
    {
        public static NatsInlineKey Empty()
        {
            return new NatsInlineKey(ReadOnlySpan<byte>.Empty);
        }

        public readonly ReadOnlySpan<byte> Span;

        public NatsInlineKey(ReadOnlySpan<byte> span)
        {
            Span = span;
        }

        public string AsString()
        {
            return Encoding.UTF8.GetString(Span);
        }

    }

}

