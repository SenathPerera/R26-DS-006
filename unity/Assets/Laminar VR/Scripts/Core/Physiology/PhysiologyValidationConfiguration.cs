using System;

namespace LaminarVR.AdaptiveMeditation.Physiology
{
    public sealed class PhysiologyValidationConfiguration
    {
        public PhysiologyValidationConfiguration(
            string configurationId,
            int configurationVersion,
            double staleAfterSeconds,
            double minimumWindowDurationSeconds,
            double maximumFutureClockSkewSeconds,
            double sourceTimestampToleranceSeconds,
            double probabilitySumTolerance,
            double minimumDecisionSignalQuality,
            double minimumRewardSignalQuality,
            int maximumBufferedWindows)
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
                    nameof(configurationVersion),
                    configurationVersion,
                    "Configuration version must be at least 1.");
            }

            ValidatePositive(staleAfterSeconds, nameof(staleAfterSeconds));
            ValidatePositive(
                minimumWindowDurationSeconds,
                nameof(minimumWindowDurationSeconds));
            ValidateNonNegative(
                maximumFutureClockSkewSeconds,
                nameof(maximumFutureClockSkewSeconds));
            ValidateNonNegative(
                sourceTimestampToleranceSeconds,
                nameof(sourceTimestampToleranceSeconds));
            ValidateUnitIntervalExclusiveZero(
                probabilitySumTolerance,
                nameof(probabilitySumTolerance));
            ValidateUnitInterval(
                minimumDecisionSignalQuality,
                nameof(minimumDecisionSignalQuality));
            ValidateUnitInterval(
                minimumRewardSignalQuality,
                nameof(minimumRewardSignalQuality));

            if (maximumBufferedWindows < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumBufferedWindows),
                    maximumBufferedWindows,
                    "At least one physiology window must be buffered.");
            }

            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            StaleAfterSeconds = staleAfterSeconds;
            MinimumWindowDurationSeconds = minimumWindowDurationSeconds;
            MaximumFutureClockSkewSeconds = maximumFutureClockSkewSeconds;
            SourceTimestampToleranceSeconds = sourceTimestampToleranceSeconds;
            ProbabilitySumTolerance = probabilitySumTolerance;
            MinimumDecisionSignalQuality = minimumDecisionSignalQuality;
            MinimumRewardSignalQuality = minimumRewardSignalQuality;
            MaximumBufferedWindows = maximumBufferedWindows;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public double StaleAfterSeconds { get; }

        public double MinimumWindowDurationSeconds { get; }

        public double MaximumFutureClockSkewSeconds { get; }

        public double SourceTimestampToleranceSeconds { get; }

        public double ProbabilitySumTolerance { get; }

        public double MinimumDecisionSignalQuality { get; }

        public double MinimumRewardSignalQuality { get; }

        public int MaximumBufferedWindows { get; }

        private static void ValidatePositive(double value, string parameterName)
        {
            if (!IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Value must be finite and greater than 0.");
            }
        }

        private static void ValidateNonNegative(double value, string parameterName)
        {
            if (!IsFinite(value) || value < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Value must be finite and non-negative.");
            }
        }

        private static void ValidateUnitInterval(double value, string parameterName)
        {
            if (!IsFinite(value) || value < 0d || value > 1d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Value must be finite and within [0, 1].");
            }
        }

        private static void ValidateUnitIntervalExclusiveZero(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value <= 0d || value > 1d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Value must be finite, greater than 0, and no greater than 1.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

