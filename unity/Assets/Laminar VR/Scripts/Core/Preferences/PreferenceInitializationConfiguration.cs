using System;

namespace LaminarVR.AdaptiveMeditation.Preferences
{
    public sealed class PreferenceInitializationConfiguration
    {
        public PreferenceInitializationConfiguration(
            string configurationId,
            int configurationVersion,
            double preferenceWeight)
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

            if (double.IsNaN(preferenceWeight)
                || double.IsInfinity(preferenceWeight)
                || preferenceWeight < 0d
                || preferenceWeight > 1d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(preferenceWeight),
                    preferenceWeight,
                    "Preference weight must be finite and within [0, 1].");
            }

            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            PreferenceWeight = preferenceWeight;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public double PreferenceWeight { get; }
    }
}

