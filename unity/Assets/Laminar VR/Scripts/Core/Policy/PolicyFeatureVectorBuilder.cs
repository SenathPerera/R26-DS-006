using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public sealed class PolicyFeatureVectorBuilder : IFeatureVectorBuilder
    {
        public const string DraftSchemaVersion =
            "adaptive-vr-policy-features/0.1-draft";

        private const int DraftFeatureCount = 24;
        private static readonly double MaximumEnvironmentEuclideanDistance =
            Math.Sqrt(5d);

        public int FeatureCount => DraftFeatureCount;

        public string FeatureSchemaVersion => DraftSchemaVersion;

        public FeatureVector Build(PolicyObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            // TODO(RESEARCH_DECISION): Freeze schema 1.0 only after baseline,
            // context, scene, history, and missing-value encodings are approved.
            var stress = observation.Physiology.Window.Stress;
            var probabilities = stress.Probabilities;
            var preferred = observation.PreferredEnvironment;
            var current = observation.CurrentEnvironment;
            var safeDefault = observation.SafeDefaultEnvironment;
            var values = new double[DraftFeatureCount];

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
            switch (index)
            {
                case 0: return "bias";
                case 1: return "continuous_stress_score_01";
                case 2: return "stress_level_0_probability";
                case 3: return "stress_level_1_probability";
                case 4: return "stress_level_2_probability";
                case 5: return "stress_level_3_probability";
                case 6: return "signal_quality";
                case 7: return "preferred_illumination";
                case 8: return "preferred_warmth";
                case 9: return "preferred_atmospheric_softness";
                case 10: return "preferred_color_richness";
                case 11: return "preferred_ambient_motion";
                case 12: return "current_illumination";
                case 13: return "current_warmth";
                case 14: return "current_atmospheric_softness";
                case 15: return "current_color_richness";
                case 16: return "current_ambient_motion";
                case 17: return "illumination_delta_from_preference";
                case 18: return "warmth_delta_from_preference";
                case 19: return "atmospheric_softness_delta_from_preference";
                case 20: return "color_richness_delta_from_preference";
                case 21: return "ambient_motion_delta_from_preference";
                case 22: return "distance_from_preference_01";
                case 23: return "distance_from_safe_default_01";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(index),
                        index,
                        "Feature index is outside the draft schema.");
            }
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

