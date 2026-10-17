using System;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    /// <summary>
    /// Platform-independent PCG-XSH-RR 32-bit generator. The initializer and
    /// output transform match the reference PCG32 construction.
    /// </summary>
    public sealed class Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong state;
        private readonly ulong increment;

        public Pcg32(ulong initialState, ulong streamSequence)
        {
            increment = unchecked((streamSequence << 1) | 1UL);
            state = 0UL;
            NextUInt();
            state = unchecked(state + initialState);
            NextUInt();
        }

        public uint NextUInt()
        {
            var oldState = state;
            state = unchecked((oldState * Multiplier) + increment);
            var xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            var rotation = (int)(oldState >> 59);
            return (xorShifted >> rotation)
                | (xorShifted << ((-rotation) & 31));
        }

        public double NextUnitDouble()
        {
            var high = NextUInt() >> 5;
            var low = NextUInt() >> 6;
            return ((high * 67108864d) + low) / 9007199254740992d;
        }

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMaximum));
            }

            var bound = (uint)exclusiveMaximum;
            var threshold = unchecked(0u - bound) % bound;
            uint value;
            do
            {
                value = NextUInt();
            }
            while (value < threshold);

            return (int)(value % bound);
        }

        public double NextDouble(double minimum, double maximum)
        {
            if (!IsFinite(minimum)
                || !IsFinite(maximum)
                || maximum < minimum)
            {
                throw new ArgumentException(
                    "A finite, ordered random range is required.");
            }

            return minimum + ((maximum - minimum) * NextUnitDouble());
        }

        public double NextGaussian(double mean, double standardDeviation)
        {
            if (!IsFinite(mean)
                || !IsFinite(standardDeviation)
                || standardDeviation < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(standardDeviation));
            }

            if (standardDeviation == 0d)
            {
                return mean;
            }

            // 1-u is in (0, 1], avoiding log(0) without a retry branch.
            var radius = Math.Sqrt(-2d * Math.Log(1d - NextUnitDouble()));
            var angle = 2d * Math.PI * NextUnitDouble();
            return mean + (standardDeviation * radius * Math.Cos(angle));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
