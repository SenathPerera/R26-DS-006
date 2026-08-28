using System;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class ReconnectBackoffConfiguration
    {
        public ReconnectBackoffConfiguration(
            string configurationId,
            int configurationVersion,
            int maximumAttempts,
            double initialDelaySeconds,
            double maximumDelaySeconds,
            double delayMultiplier)
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

            if (maximumAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumAttempts),
                    maximumAttempts,
                    "Maximum reconnect attempts must be at least 1.");
            }

            ValidateNonNegativeFinite(
                initialDelaySeconds,
                nameof(initialDelaySeconds));
            ValidateNonNegativeFinite(
                maximumDelaySeconds,
                nameof(maximumDelaySeconds));
            if (maximumDelaySeconds < initialDelaySeconds)
            {
                throw new ArgumentException(
                    "Maximum delay must be at least the initial delay.",
                    nameof(maximumDelaySeconds));
            }

            if (double.IsNaN(delayMultiplier)
                || double.IsInfinity(delayMultiplier)
                || delayMultiplier < 1d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delayMultiplier),
                    delayMultiplier,
                    "Reconnect delay multiplier must be finite and at least 1.");
            }

            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            MaximumAttempts = maximumAttempts;
            InitialDelaySeconds = initialDelaySeconds;
            MaximumDelaySeconds = maximumDelaySeconds;
            DelayMultiplier = delayMultiplier;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public int MaximumAttempts { get; }

        public double InitialDelaySeconds { get; }

        public double MaximumDelaySeconds { get; }

        public double DelayMultiplier { get; }

        public double GetDelaySeconds(int attemptNumber)
        {
            if (attemptNumber < 1 || attemptNumber > MaximumAttempts)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attemptNumber),
                    attemptNumber,
                    "Attempt number is outside the configured reconnect schedule.");
            }

            var delaySeconds = InitialDelaySeconds;
            for (var attempt = 1; attempt < attemptNumber; attempt++)
            {
                if (delaySeconds >= MaximumDelaySeconds
                    || delaySeconds > MaximumDelaySeconds / DelayMultiplier)
                {
                    return MaximumDelaySeconds;
                }

                delaySeconds *= DelayMultiplier;
            }

            return Math.Min(delaySeconds, MaximumDelaySeconds);
        }

        private static void ValidateNonNegativeFinite(
            double value,
            string parameterName)
        {
            if (double.IsNaN(value)
                || double.IsInfinity(value)
                || value < 0d
                || value > TimeSpan.MaxValue.TotalSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Reconnect delays must fit a non-negative TimeSpan.");
            }
        }
    }
}
