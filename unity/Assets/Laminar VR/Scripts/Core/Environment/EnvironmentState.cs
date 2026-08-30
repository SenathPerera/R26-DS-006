using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    [Serializable]
    public readonly struct EnvironmentState : IEquatable<EnvironmentState>
    {
        public EnvironmentState(
            float illumination,
            float warmth,
            float atmosphericSoftness,
            float colorRichness,
            float ambientMotion)
        {
            EnsureFinite(illumination, nameof(illumination));
            EnsureFinite(warmth, nameof(warmth));
            EnsureFinite(atmosphericSoftness, nameof(atmosphericSoftness));
            EnsureFinite(colorRichness, nameof(colorRichness));
            EnsureFinite(ambientMotion, nameof(ambientMotion));

            Illumination = illumination;
            Warmth = warmth;
            AtmosphericSoftness = atmosphericSoftness;
            ColorRichness = colorRichness;
            AmbientMotion = ambientMotion;
        }

        public float Illumination { get; }

        public float Warmth { get; }

        public float AtmosphericSoftness { get; }

        public float ColorRichness { get; }

        public float AmbientMotion { get; }

        public bool IsNormalized =>
            IsNormalizedValue(Illumination)
            && IsNormalizedValue(Warmth)
            && IsNormalizedValue(AtmosphericSoftness)
            && IsNormalizedValue(ColorRichness)
            && IsNormalizedValue(AmbientMotion);

        public EnvironmentState Clamp01()
        {
            return new EnvironmentState(
                Clamp01(Illumination),
                Clamp01(Warmth),
                Clamp01(AtmosphericSoftness),
                Clamp01(ColorRichness),
                Clamp01(AmbientMotion));
        }

        public double L1DistanceTo(EnvironmentState other)
        {
            return Math.Abs((double)Illumination - other.Illumination)
                + Math.Abs((double)Warmth - other.Warmth)
                + Math.Abs((double)AtmosphericSoftness - other.AtmosphericSoftness)
                + Math.Abs((double)ColorRichness - other.ColorRichness)
                + Math.Abs((double)AmbientMotion - other.AmbientMotion);
        }

        public double EuclideanDistanceTo(EnvironmentState other)
        {
            var illuminationDifference = (double)Illumination - other.Illumination;
            var warmthDifference = (double)Warmth - other.Warmth;
            var softnessDifference =
                (double)AtmosphericSoftness - other.AtmosphericSoftness;
            var richnessDifference = (double)ColorRichness - other.ColorRichness;
            var motionDifference = (double)AmbientMotion - other.AmbientMotion;

            return Math.Sqrt(
                (illuminationDifference * illuminationDifference)
                + (warmthDifference * warmthDifference)
                + (softnessDifference * softnessDifference)
                + (richnessDifference * richnessDifference)
                + (motionDifference * motionDifference));
        }

        public bool ApproximatelyEquals(EnvironmentState other, float tolerance)
        {
            EnsureFinite(tolerance, nameof(tolerance));

            if (tolerance < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tolerance),
                    tolerance,
                    "Tolerance must be non-negative.");
            }

            return Math.Abs(Illumination - other.Illumination) <= tolerance
                && Math.Abs(Warmth - other.Warmth) <= tolerance
                && Math.Abs(AtmosphericSoftness - other.AtmosphericSoftness) <= tolerance
                && Math.Abs(ColorRichness - other.ColorRichness) <= tolerance
                && Math.Abs(AmbientMotion - other.AmbientMotion) <= tolerance;
        }

        public bool Equals(EnvironmentState other)
        {
            return Illumination.Equals(other.Illumination)
                && Warmth.Equals(other.Warmth)
                && AtmosphericSoftness.Equals(other.AtmosphericSoftness)
                && ColorRichness.Equals(other.ColorRichness)
                && AmbientMotion.Equals(other.AmbientMotion);
        }

        public override bool Equals(object obj)
        {
            return obj is EnvironmentState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Illumination.GetHashCode();
                hashCode = (hashCode * 397) ^ Warmth.GetHashCode();
                hashCode = (hashCode * 397) ^ AtmosphericSoftness.GetHashCode();
                hashCode = (hashCode * 397) ^ ColorRichness.GetHashCode();
                hashCode = (hashCode * 397) ^ AmbientMotion.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(EnvironmentState left, EnvironmentState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EnvironmentState left, EnvironmentState right)
        {
            return !left.Equals(right);
        }

        private static bool IsNormalizedValue(float value)
        {
            return value >= 0f && value <= 1f;
        }

        private static float Clamp01(float value)
        {
            return Math.Min(1f, Math.Max(0f, value));
        }

        private static void EnsureFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Environment values must be finite.");
            }
        }
    }
}
