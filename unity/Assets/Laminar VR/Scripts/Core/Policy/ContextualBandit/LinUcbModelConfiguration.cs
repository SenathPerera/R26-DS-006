using System;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public sealed class LinUcbModelConfiguration
    {
        public LinUcbModelConfiguration(
            string configurationId,
            int configurationVersion,
            string featureSchemaVersion,
            int featureCount,
            double ridgeRegularization,
            double explorationCoefficient)
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

            if (string.IsNullOrWhiteSpace(featureSchemaVersion))
            {
                throw new ArgumentException(
                    "Feature schema version is required.",
                    nameof(featureSchemaVersion));
            }

            if (featureCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(featureCount));
            }

            ValidatePositive(
                ridgeRegularization,
                nameof(ridgeRegularization));
            ValidateNonNegative(
                explorationCoefficient,
                nameof(explorationCoefficient));

            // Research use depends on an approved, versioned profile supplying
            // these hyperparameters.
            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            FeatureSchemaVersion = featureSchemaVersion.Trim();
            FeatureCount = featureCount;
            RidgeRegularization = ridgeRegularization;
            ExplorationCoefficient = explorationCoefficient;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public string FeatureSchemaVersion { get; }

        public int FeatureCount { get; }

        public double RidgeRegularization { get; }

        public double ExplorationCoefficient { get; }

        public string ModelVersion =>
            ConfigurationId + "/" + ConfigurationVersion;

        private static void ValidatePositive(
            double value,
            string parameterName)
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
