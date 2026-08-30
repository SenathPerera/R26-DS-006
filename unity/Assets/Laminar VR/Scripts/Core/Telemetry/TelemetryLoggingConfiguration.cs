using System;

namespace LaminarVR.AdaptiveMeditation.Telemetry
{
    public sealed class TelemetryLoggingConfiguration
    {
        public TelemetryLoggingConfiguration(
            string configurationId,
            int configurationVersion,
            string eventSchemaId,
            string eventSchemaVersion,
            int flushEveryEventCount)
        {
            ConfigurationId = RequireText(
                configurationId,
                nameof(configurationId));
            if (configurationVersion < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configurationVersion),
                    configurationVersion,
                    "Configuration version must be at least 1.");
            }

            EventSchemaId = RequireText(eventSchemaId, nameof(eventSchemaId));
            EventSchemaVersion = RequireText(
                eventSchemaVersion,
                nameof(eventSchemaVersion));
            if (flushEveryEventCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(flushEveryEventCount),
                    flushEveryEventCount,
                    "Flush event count must be at least 1.");
            }

            ConfigurationVersion = configurationVersion;
            FlushEveryEventCount = flushEveryEventCount;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public string EventSchemaId { get; }

        public string EventSchemaVersion { get; }

        public int FlushEveryEventCount { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    parameterName);
            }

            return value.Trim();
        }
    }
}
