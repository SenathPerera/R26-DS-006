using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    [Serializable]
    public readonly struct EnvironmentStateLimits
    {
        public EnvironmentStateLimits(
            NormalizedRange illumination,
            NormalizedRange warmth,
            NormalizedRange atmosphericSoftness,
            NormalizedRange colorRichness,
            NormalizedRange ambientMotion)
        {
            Illumination = illumination;
            Warmth = warmth;
            AtmosphericSoftness = atmosphericSoftness;
            ColorRichness = colorRichness;
            AmbientMotion = ambientMotion;
        }

        public NormalizedRange Illumination { get; }

        public NormalizedRange Warmth { get; }

        public NormalizedRange AtmosphericSoftness { get; }

        public NormalizedRange ColorRichness { get; }

        public NormalizedRange AmbientMotion { get; }

        public bool Contains(EnvironmentState state)
        {
            return Illumination.Contains(state.Illumination)
                && Warmth.Contains(state.Warmth)
                && AtmosphericSoftness.Contains(state.AtmosphericSoftness)
                && ColorRichness.Contains(state.ColorRichness)
                && AmbientMotion.Contains(state.AmbientMotion);
        }

        public EnvironmentState Clamp(EnvironmentState state)
        {
            return new EnvironmentState(
                Illumination.Clamp(state.Illumination),
                Warmth.Clamp(state.Warmth),
                AtmosphericSoftness.Clamp(state.AtmosphericSoftness),
                ColorRichness.Clamp(state.ColorRichness),
                AmbientMotion.Clamp(state.AmbientMotion));
        }

        public bool TryIntersect(
            EnvironmentStateLimits other,
            out EnvironmentStateLimits intersection)
        {
            if (!Illumination.TryIntersect(other.Illumination, out var illumination)
                || !Warmth.TryIntersect(other.Warmth, out var warmth)
                || !AtmosphericSoftness.TryIntersect(
                    other.AtmosphericSoftness,
                    out var atmosphericSoftness)
                || !ColorRichness.TryIntersect(
                    other.ColorRichness,
                    out var colorRichness)
                || !AmbientMotion.TryIntersect(
                    other.AmbientMotion,
                    out var ambientMotion))
            {
                intersection = default;
                return false;
            }

            intersection = new EnvironmentStateLimits(
                illumination,
                warmth,
                atmosphericSoftness,
                colorRichness,
                ambientMotion);
            return true;
        }
    }
}
