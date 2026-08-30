using System;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.Rewards
{
    public sealed class RewardPipelineConfiguration
    {
        public RewardPipelineConfiguration(
            string configurationId,
            int configurationVersion,
            BaselineStandardDeviationMethod baselineStandardDeviationMethod,
            int minimumBaselineSamples,
            double minimumBaselineStandardDeviation,
            int trendWindowCount,
            int minimumTrendSamples,
            double settlingSeconds,
            double maximumAttributionWaitSeconds,
            double stressWeight,
            double rmssdWeight,
            double heartRateWeight,
            double changePenaltyWeight,
            double discomfortPenaltyWeight,
            double safetyPenaltyWeight)
        {
            if (string.IsNullOrWhiteSpace(configurationId))
            {
                throw new ArgumentException(
                    "Configuration ID is required.",
                    nameof(configurationId));
            }

            if (configurationVersion < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configurationVersion));
            }

            if (!Enum.IsDefined(
                typeof(BaselineStandardDeviationMethod),
                baselineStandardDeviationMethod))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(baselineStandardDeviationMethod));
            }

            if (minimumBaselineSamples < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumBaselineSamples),
                    minimumBaselineSamples,
                    "At least two baseline samples are required.");
            }

            ValidatePositive(
                minimumBaselineStandardDeviation,
                nameof(minimumBaselineStandardDeviation));

            if (trendWindowCount < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(trendWindowCount));
            }

            if (minimumTrendSamples < 2
                || minimumTrendSamples > trendWindowCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumTrendSamples));
            }

            ValidateNonNegative(settlingSeconds, nameof(settlingSeconds));
            ValidatePositive(
                maximumAttributionWaitSeconds,
                nameof(maximumAttributionWaitSeconds));
            if (maximumAttributionWaitSeconds <= settlingSeconds)
            {
                throw new ArgumentException(
                    "Maximum attribution wait must exceed settling time.",
                    nameof(maximumAttributionWaitSeconds));
            }

            ValidateNonNegative(stressWeight, nameof(stressWeight));
            ValidateNonNegative(rmssdWeight, nameof(rmssdWeight));
            ValidateNonNegative(heartRateWeight, nameof(heartRateWeight));
            ValidateNonNegative(
                changePenaltyWeight,
                nameof(changePenaltyWeight));
            ValidateNonNegative(
                discomfortPenaltyWeight,
                nameof(discomfortPenaltyWeight));
            ValidateNonNegative(
                safetyPenaltyWeight,
                nameof(safetyPenaltyWeight));

            if (stressWeight == 0d
                && rmssdWeight == 0d
                && heartRateWeight == 0d)
            {
                throw new ArgumentException(
                    "At least one physiological reward weight must be positive.");
            }

            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            BaselineStandardDeviationMethod =
                baselineStandardDeviationMethod;
            MinimumBaselineSamples = minimumBaselineSamples;
            MinimumBaselineStandardDeviation =
                minimumBaselineStandardDeviation;
            TrendWindowCount = trendWindowCount;
            MinimumTrendSamples = minimumTrendSamples;
            SettlingSeconds = settlingSeconds;
            MaximumAttributionWaitSeconds = maximumAttributionWaitSeconds;
            StressWeight = stressWeight;
            RmssdWeight = rmssdWeight;
            HeartRateWeight = heartRateWeight;
            ChangePenaltyWeight = changePenaltyWeight;
            DiscomfortPenaltyWeight = discomfortPenaltyWeight;
            SafetyPenaltyWeight = safetyPenaltyWeight;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public BaselineStandardDeviationMethod
            BaselineStandardDeviationMethod { get; }

        public int MinimumBaselineSamples { get; }

        public double MinimumBaselineStandardDeviation { get; }

        public int TrendWindowCount { get; }

        public int MinimumTrendSamples { get; }

        public double SettlingSeconds { get; }

        public double MaximumAttributionWaitSeconds { get; }

        public double StressWeight { get; }

        public double RmssdWeight { get; }

        public double HeartRateWeight { get; }

        public double ChangePenaltyWeight { get; }

        public double DiscomfortPenaltyWeight { get; }

        public double SafetyPenaltyWeight { get; }

        private static void ValidatePositive(double value, string parameterName)
        {
            if (!IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateNonNegative(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
