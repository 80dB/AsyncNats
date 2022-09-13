using System;

namespace EightyDecibel.AsyncNats
{
    public static class CountDigitsExtension
    {
        public static int CountDigits(this int value)
        {
            if (value < 10) return 1;
            if (value < 100) return 2;
            if (value < 1_000) return 3;
            if (value < 10_000) return 4;
            if (value < 100_000) return 5;
            if (value < 1_000_000) return 6;
            if (value < 10_000_000) return 7;
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}