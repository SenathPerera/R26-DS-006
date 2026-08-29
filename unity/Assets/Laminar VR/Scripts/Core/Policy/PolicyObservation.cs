using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public sealed class PolicyObservation
    {
        public PolicyObservation(
            PhysiologyWindowSnapshot physiology,
            EnvironmentState preferredEnvironment,
            EnvironmentState currentEnvironment,
            EnvironmentState safeDefaultEnvironment)
            : this(
                physiology,
                preferredEnvironment,
                currentEnvironment,
                safeDefaultEnvironment,
                null)
        {
        }

        public PolicyObservation(
            PhysiologyWindowSnapshot physiology,
            EnvironmentState preferredEnvironment,
            EnvironmentState currentEnvironment,
            EnvironmentState safeDefaultEnvironment,
            PhysiologyTrendResult? physiologyTrend)
        {
            if (physiology.SequenceNumber < 1L || physiology.Window == null)
            {
                throw new ArgumentException(
                    "A validated physiology window snapshot is required.",
                    nameof(physiology));
            }

            ValidateNormalized(
                preferredEnvironment,
                nameof(preferredEnvironment));
            ValidateNormalized(currentEnvironment, nameof(currentEnvironment));
            ValidateNormalized(
                safeDefaultEnvironment,
                nameof(safeDefaultEnvironment));

            var stress = physiology.Window.Stress;
            if (stress == null
                || !stress.Probabilities.IsFiniteAndInUnitRange
                || !IsFiniteInRange(stress.ContinuousScore, 0d, 3d)
                || !IsFiniteInRange(
                    physiology.Window.SignalQuality,
                    0d,
                    1d))
            {
                throw new ArgumentException(
                    "The physiology snapshot does not contain bounded policy inputs.",
                    nameof(physiology));
            }

            if (physiologyTrend.HasValue
                && physiologyTrend.Value.Available
                && (physiologyTrend.Value.SampleCount < 2
                    || physiologyTrend.Value.LastSequenceNumber
                        != physiology.SequenceNumber
                    || !IsFinite(
                        physiologyTrend.Value.StressScorePerMinute)
                    || !IsFinite(
                        physiologyTrend.Value.HeartRateBpmPerMinute)
                    || (physiologyTrend.Value.RmssdMsPerMinute.HasValue
                        && !IsFinite(
                            physiologyTrend.Value.RmssdMsPerMinute.Value))))
            {
                throw new ArgumentException(
                    "An available trend must end at the observation window.",
                    nameof(physiologyTrend));
            }

            Physiology = physiology;
            PreferredEnvironment = preferredEnvironment;
            CurrentEnvironment = currentEnvironment;
            SafeDefaultEnvironment = safeDefaultEnvironment;
            PhysiologyTrend = physiologyTrend;
        }

        public PhysiologyWindowSnapshot Physiology { get; }

        public EnvironmentState PreferredEnvironment { get; }

        public EnvironmentState CurrentEnvironment { get; }

        public EnvironmentState SafeDefaultEnvironment { get; }

        public PhysiologyTrendResult? PhysiologyTrend { get; }

        private static void ValidateNormalized(
            EnvironmentState state,
            string parameterName)
        {
            if (!state.IsNormalized)
            {
                throw new ArgumentException(
                    "Policy environment inputs must be normalized.",
                    parameterName);
            }
        }

        private static bool IsFiniteInRange(
            double value,
            double minimum,
            double maximum)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= minimum
                && value <= maximum;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

