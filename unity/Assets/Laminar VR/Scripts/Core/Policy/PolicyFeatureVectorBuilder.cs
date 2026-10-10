using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public sealed class PolicyFeatureVectorBuilder : IFeatureVectorBuilder
    {
        public const string ProductionSchemaVersion =
            "adaptive-vr-policy-features/1.0";

        public const int ProductionFeatureCount = 24;

        private static readonly string[] ProductionFeatureNames =
        {
            "bias",
            "continuous_stress_score_01",
            "stress_level_0_probability",
            "stress_level_1_probability",
            "stress_level_2_probability",
            "stress_level_3_probability",
            "signal_quality",
            "preferred_illumination",
            "preferred_warmth",
            "preferred_atmospheric_softness",
            "preferred_color_richness",
            "preferred_ambient_motion",
            "current_illumination",
            "current_warmth",
            "current_atmospheric_softness",
            "current_color_richness",
            "current_ambient_motion",
            "illumination_delta_from_preference",
            "warmth_delta_from_preference",
            "atmospheric_softness_delta_from_preference",
            "color_richness_delta_from_preference",
            "ambient_motion_delta_from_preference",
            "distance_from_preference_01",
            "distance_from_safe_default_01"
        };

        private static readonly double MaximumEnvironmentEuclideanDistance =
            Math.Sqrt(5d);

        public int FeatureCount => ProductionFeatureCount;

        public string FeatureSchemaVersion => ProductionSchemaVersion;

        public FeatureVector Build(PolicyObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            var stress = observation.Physiology.Window.Stress;
            var probabilities = stress.Probabilities;
            var preferred = observation.PreferredEnvironment;
            var current = observation.CurrentEnvironment;
            var safeDefault = observation.SafeDefaultEnvironment;
            var values = new double[ProductionFeatureCount];

            values[0] = 1d;
            values[1] = stress.ContinuousScore / 3d;
            values[2] = probabilities.Level0Probability;
            values[3] = probabilities.Level1Probability;
            values[4] = probabilities.Level2Probability;
            values[5] = probabilities.Level3Probability;
            values[6] = observation.Physiology.Window.SignalQuality;

            WriteEnvironment(values, 7, preferred);
            WriteEnvironment(values, 12, current);
            WriteEnvironmentDelta(values, 17, current, preferred);
            values[22] = current.EuclideanDistanceTo(preferred)
                / MaximumEnvironmentEuclideanDistance;
            values[23] = current.EuclideanDistanceTo(safeDefault)
                / MaximumEnvironmentEuclideanDistance;

            return new FeatureVector(FeatureSchemaVersion, values);
        }

        public string GetFeatureName(int index)
        {
            if (index < 0 || index >= ProductionFeatureNames.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "Feature index is outside the production schema.");
            }

            return ProductionFeatureNames[index];
        }

        private static void WriteEnvironment(
            double[] values,
            int startIndex,
            EnvironmentState state)
        {
            values[startIndex] = state.Illumination;
            values[startIndex + 1] = state.Warmth;
            values[startIndex + 2] = state.AtmosphericSoftness;
            values[startIndex + 3] = state.ColorRichness;
            values[startIndex + 4] = state.AmbientMotion;
        }

        private static void WriteEnvironmentDelta(
            double[] values,
            int startIndex,
            EnvironmentState current,
            EnvironmentState preferred)
        {
            values[startIndex] = current.Illumination - preferred.Illumination;
            values[startIndex + 1] = current.Warmth - preferred.Warmth;
            values[startIndex + 2] =
                current.AtmosphericSoftness - preferred.AtmosphericSoftness;
            values[startIndex + 3] =
                current.ColorRichness - preferred.ColorRichness;
            values[startIndex + 4] =
                current.AmbientMotion - preferred.AmbientMotion;
        }
    }
}

