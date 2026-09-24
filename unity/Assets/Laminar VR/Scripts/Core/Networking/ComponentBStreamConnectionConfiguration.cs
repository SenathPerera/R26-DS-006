using System;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class ComponentBStreamConnectionConfiguration
    {
        public ComponentBStreamConnectionConfiguration(
            string configurationId,
            int configurationVersion,
            string streamEndpoint,
            double keepaliveIntervalSeconds,
            int maximumMessageBytes)
        {
            if (string.IsNullOrWhiteSpace(configurationId))
            {
                throw new ArgumentException(
                    "Component B connection configuration ID is required.",
                    nameof(configurationId));
            }

            if (configurationVersion < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configurationVersion));
            }

            if (!Uri.TryCreate(
                    streamEndpoint?.Trim(),
                    UriKind.Absolute,
                    out var endpoint)
                || (!string.Equals(
                        endpoint.Scheme,
                        "ws",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        endpoint.Scheme,
                        "wss",
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "Component B requires an absolute ws:// or wss:// endpoint.",
                    nameof(streamEndpoint));
            }

            if (!IsFinite(keepaliveIntervalSeconds)
                || keepaliveIntervalSeconds <= 0d
                || keepaliveIntervalSeconds > TimeSpan.MaxValue.TotalSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(keepaliveIntervalSeconds));
            }

            if (maximumMessageBytes < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumMessageBytes));
            }

            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            StreamEndpoint = endpoint;
            KeepaliveIntervalSeconds = keepaliveIntervalSeconds;
            MaximumMessageBytes = maximumMessageBytes;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public Uri StreamEndpoint { get; }

        public double KeepaliveIntervalSeconds { get; }

        public int MaximumMessageBytes { get; }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
