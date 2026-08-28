using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    [Serializable]
    public readonly struct NormalizedRange : IEquatable<NormalizedRange>
    {
        public NormalizedRange(float minimum, float maximum)
        {
            EnsureFinite(minimum, nameof(minimum));
            EnsureFinite(maximum, nameof(maximum));

            if (minimum < 0f || maximum > 1f || minimum > maximum)
            {
                throw new ArgumentException(
                    "A normalized range must satisfy 0 <= minimum <= maximum <= 1.");
            }

            Minimum = minimum;
            Maximum = maximum;
        }

        public float Minimum { get; }

        public float Maximum { get; }

        public bool Contains(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value)
                && value >= Minimum
                && value <= Maximum;
        }

        public float Clamp(float value)
        {
            EnsureFinite(value, nameof(value));
            return Math.Min(Maximum, Math.Max(Minimum, value));
        }

        public bool Equals(NormalizedRange other)
        {
            return Minimum.Equals(other.Minimum) && Maximum.Equals(other.Maximum);
        }

        public override bool Equals(object obj)
        {
            return obj is NormalizedRange other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Minimum.GetHashCode() * 397) ^ Maximum.GetHashCode();
            }
        }

        public static bool operator ==(NormalizedRange left, NormalizedRange right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(NormalizedRange left, NormalizedRange right)
        {
            return !left.Equals(right);
        }

        private static void EnsureFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Normalized range values must be finite.");
            }
        }
    }
}
