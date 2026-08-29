using System;

namespace LaminarVR.AdaptiveMeditation.Policy.RuleBased
{
    public enum RuleActivationMode
    {
        WorseningStressTrend,
        ElevatedStress,
        WorseningTrendOrElevatedStress,
        WorseningTrendAndElevatedStress
    }

    public sealed class RuleBasedPolicyConfiguration
    {
        public RuleBasedPolicyConfiguration(
            string configurationId,
            int configurationVersion,
            RuleActivationMode activationMode,
            double minimumContinuousStressScore,
            double minimumStressIncreasePerMinute,
            double minimumPreferenceDelta)
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

            if (!Enum.IsDefined(typeof(RuleActivationMode), activationMode))
            {
                throw new ArgumentOutOfRangeException(nameof(activationMode));
            }

            ValidateExclusiveZeroRange(
                minimumContinuousStressScore,
                3d,
                nameof(minimumContinuousStressScore));
            ValidatePositive(
                minimumStressIncreasePerMinute,
                nameof(minimumStressIncreasePerMinute));
            ValidateExclusiveZeroUnitInterval(
                minimumPreferenceDelta,
                nameof(minimumPreferenceDelta));

            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            ActivationMode = activationMode;
            MinimumContinuousStressScore =
                minimumContinuousStressScore;
            MinimumStressIncreasePerMinute =
                minimumStressIncreasePerMinute;
            MinimumPreferenceDelta = minimumPreferenceDelta;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public RuleActivationMode ActivationMode { get; }

        public double MinimumContinuousStressScore { get; }

        public double MinimumStressIncreasePerMinute { get; }

        public double MinimumPreferenceDelta { get; }

        private static void ValidateExclusiveZeroUnitInterval(
            double value,
            string parameterName)
        {
            ValidateExclusiveZeroRange(value, 1d, parameterName);
        }

        private static void ValidateExclusiveZeroRange(
            double value,
            double maximum,
            string parameterName)
        {
            if (!IsFinite(value) || value <= 0d || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidatePositive(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value <= 0d)
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
