using System;

namespace LaminarVR.AdaptiveMeditation.Session
{
    public sealed class SessionTimingConfiguration
    {
        public SessionTimingConfiguration(
            string configurationId,
            int configurationVersion,
            double acclimatizationDurationSeconds,
            double adaptiveDurationSeconds,
            double stabilizationDurationSeconds,
            double decisionIntervalSeconds)
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

            ValidatePositiveDuration(
                acclimatizationDurationSeconds,
                nameof(acclimatizationDurationSeconds));
            ValidatePositiveDuration(
                adaptiveDurationSeconds,
                nameof(adaptiveDurationSeconds));
            ValidatePositiveDuration(
                stabilizationDurationSeconds,
                nameof(stabilizationDurationSeconds));
            ValidatePositiveDuration(
                decisionIntervalSeconds,
                nameof(decisionIntervalSeconds));

            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            AcclimatizationDurationSeconds = acclimatizationDurationSeconds;
            AdaptiveDurationSeconds = adaptiveDurationSeconds;
            StabilizationDurationSeconds = stabilizationDurationSeconds;
            DecisionIntervalSeconds = decisionIntervalSeconds;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public double AcclimatizationDurationSeconds { get; }

        public double AdaptiveDurationSeconds { get; }

        public double StabilizationDurationSeconds { get; }

        public double DecisionIntervalSeconds { get; }

        public double TimedSessionDurationSeconds =>
            AcclimatizationDurationSeconds
            + AdaptiveDurationSeconds
            + StabilizationDurationSeconds;

        private static void ValidatePositiveDuration(
            double durationSeconds,
            string parameterName)
        {
            if (double.IsNaN(durationSeconds)
                || double.IsInfinity(durationSeconds)
                || durationSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    durationSeconds,
                    "Duration must be finite and greater than 0 seconds.");
            }
        }
    }
}

