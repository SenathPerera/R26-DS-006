using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public enum SyntheticArchetypeId
    {
        A1PreferenceAlignedBalanced = 1,
        A2IlluminationDominant = 2,
        A3WarmthDominant = 3,
        A4SoftnessRichnessInteraction = 4,
        A5MotionAverseThreshold = 5,
        A6PreferencePhysiologyConflict = 6,
        A7WeakResponder = 7,
        A8NonStationaryResponder = 8,
        A9PhysiologicalNonResponder = 9
    }

    public sealed class SyntheticArchetypeDefinition
    {
        private const double WeightTolerance = 1e-9d;
        private readonly double[] weights;

        internal SyntheticArchetypeDefinition(
            SyntheticArchetypeId id,
            string description,
            double responseGain,
            params double[] weights)
        {
            if (!Enum.IsDefined(typeof(SyntheticArchetypeId), id))
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                throw new ArgumentException(
                    "An archetype description is required.",
                    nameof(description));
            }

            if (!IsFinite(responseGain) || responseGain < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(responseGain));
            }

            if (weights == null || weights.Length != 5)
            {
                throw new ArgumentException(
                    "Exactly five environment weights are required.",
                    nameof(weights));
            }

            this.weights = new double[weights.Length];
            var sum = 0d;
            for (var index = 0; index < weights.Length; index++)
            {
                if (!IsFinite(weights[index]) || weights[index] < 0d)
                {
                    throw new ArgumentOutOfRangeException(nameof(weights));
                }

                this.weights[index] = weights[index];
                sum += weights[index];
            }

            if (Math.Abs(sum - 1d) > WeightTolerance)
            {
                throw new ArgumentException(
                    "Environment weights must sum to one.",
                    nameof(weights));
            }

            Id = id;
            Description = description.Trim();
            ResponseGain = responseGain;
        }

        public SyntheticArchetypeId Id { get; }

        public string Code => "A" + (int)Id;

        public string Description { get; }

        public double ResponseGain { get; }

        public double GetWeight(int index)
        {
            return weights[index];
        }

        public double CalculateUtility(
            EnvironmentState preference,
            EnvironmentState environment,
            double sessionElapsedSeconds)
        {
            if (!preference.IsNormalized || !environment.IsNormalized)
            {
                throw new ArgumentException(
                    "Utility inputs must be normalized.");
            }

            if (!IsFinite(sessionElapsedSeconds)
                || sessionElapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sessionElapsedSeconds));
            }

            var optimum = CreateOptimum(preference, sessionElapsedSeconds);
            var closeness = new[]
            {
                1d - Math.Abs(environment.Illumination - optimum.Illumination),
                1d - Math.Abs(environment.Warmth - optimum.Warmth),
                1d - Math.Abs(
                    environment.AtmosphericSoftness
                    - optimum.AtmosphericSoftness),
                1d - Math.Abs(
                    environment.ColorRichness - optimum.ColorRichness),
                1d - Math.Abs(environment.AmbientMotion - optimum.AmbientMotion)
            };
            var baseUtility = 0d;
            for (var index = 0; index < closeness.Length; index++)
            {
                baseUtility += weights[index]
                    * closeness[index]
                    * closeness[index];
            }

            switch (Id)
            {
                case SyntheticArchetypeId.A4SoftnessRichnessInteraction:
                    return (0.5d * baseUtility)
                        + (0.5d * closeness[2] * closeness[3]);
                case SyntheticArchetypeId.A5MotionAverseThreshold:
                    return (0.8d * baseUtility)
                        + (environment.AmbientMotion <= 0.35f ? 0.2d : 0d);
                default:
                    return baseUtility;
            }
        }

        public EnvironmentState CreateOptimum(
            EnvironmentState preference,
            double sessionElapsedSeconds)
        {
            if (!preference.IsNormalized)
            {
                throw new ArgumentException(
                    "The preference must be normalized.",
                    nameof(preference));
            }

            var illumination = preference.Illumination;
            var warmth = preference.Warmth;
            var motion = preference.AmbientMotion;
            switch (Id)
            {
                case SyntheticArchetypeId.A5MotionAverseThreshold:
                    motion = Math.Min(motion, 0.2f);
                    break;
                case SyntheticArchetypeId.A6PreferencePhysiologyConflict:
                    warmth = InvertPreference(warmth);
                    motion = InvertPreference(motion);
                    break;
                case SyntheticArchetypeId.A8NonStationaryResponder:
                    if (sessionElapsedSeconds >= 570d)
                    {
                        illumination = InvertPreference(illumination);
                        warmth = InvertPreference(warmth);
                    }

                    break;
            }

            return new EnvironmentState(
                illumination,
                warmth,
                preference.AtmosphericSoftness,
                preference.ColorRichness,
                motion);
        }

        private static float InvertPreference(float value)
        {
            return Math.Min(0.8f, Math.Max(0.2f, 1f - value));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public static class SyntheticArchetypeCatalog
    {
        private static readonly SyntheticArchetypeDefinition[] Definitions =
        {
            Create(
                SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                "Preference-aligned balanced",
                4d,
                0.2d, 0.2d, 0.2d, 0.2d, 0.2d),
            Create(
                SyntheticArchetypeId.A2IlluminationDominant,
                "Illumination dominant",
                4d,
                0.6d, 0.1d, 0.1d, 0.1d, 0.1d),
            Create(
                SyntheticArchetypeId.A3WarmthDominant,
                "Warmth dominant",
                4d,
                0.1d, 0.6d, 0.1d, 0.1d, 0.1d),
            Create(
                SyntheticArchetypeId.A4SoftnessRichnessInteraction,
                "Softness/richness interaction",
                4d,
                0.1d, 0.1d, 0.35d, 0.35d, 0.1d),
            Create(
                SyntheticArchetypeId.A5MotionAverseThreshold,
                "Motion-averse threshold",
                4d,
                0.1d, 0.1d, 0.1d, 0.1d, 0.6d),
            Create(
                SyntheticArchetypeId.A6PreferencePhysiologyConflict,
                "Preference/physiology conflict",
                4d,
                0.2d, 0.2d, 0.2d, 0.2d, 0.2d),
            Create(
                SyntheticArchetypeId.A7WeakResponder,
                "Weak responder",
                0.75d,
                0.2d, 0.2d, 0.2d, 0.2d, 0.2d),
            Create(
                SyntheticArchetypeId.A8NonStationaryResponder,
                "Non-stationary responder",
                4d,
                0.2d, 0.2d, 0.2d, 0.2d, 0.2d),
            Create(
                SyntheticArchetypeId.A9PhysiologicalNonResponder,
                "Physiological non-responder",
                0d,
                0.2d, 0.2d, 0.2d, 0.2d, 0.2d)
        };

        public static int Count => Definitions.Length;

        public static SyntheticArchetypeDefinition Get(
            SyntheticArchetypeId id)
        {
            if (!Enum.IsDefined(typeof(SyntheticArchetypeId), id))
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            return Definitions[(int)id - 1];
        }

        public static SyntheticArchetypeDefinition GetAt(int index)
        {
            return Definitions[index];
        }

        private static SyntheticArchetypeDefinition Create(
            SyntheticArchetypeId id,
            string description,
            double responseGain,
            params double[] weights)
        {
            return new SyntheticArchetypeDefinition(
                id,
                description,
                responseGain,
                weights);
        }
    }
}
