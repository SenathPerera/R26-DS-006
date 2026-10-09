using System;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class SessionRelayConnectionInfo
    {
        public SessionRelayConnectionInfo(
            Uri endpoint,
            string schemaVersion,
            string pairingCode,
            string questClientId,
            string appVersion,
            int maximumMessageBytes,
            int maximumTelemetryEventsPerBatch)
        {
            Endpoint = ValidateEndpoint(endpoint);
            SchemaVersion = RequireText(
                schemaVersion,
                nameof(schemaVersion));
            PairingCode = RequireText(pairingCode, nameof(pairingCode));
            QuestClientId = RequireText(
                questClientId,
                nameof(questClientId));
            AppVersion = RequireText(appVersion, nameof(appVersion));
            if (maximumMessageBytes < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumMessageBytes));
            }

            MaximumMessageBytes = maximumMessageBytes;
            if (maximumTelemetryEventsPerBatch < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumTelemetryEventsPerBatch));
            }

            MaximumTelemetryEventsPerBatch =
                maximumTelemetryEventsPerBatch;
        }

        public Uri Endpoint { get; }

        public string SchemaVersion { get; }

        // Runtime-only credential. Do not serialize or include in diagnostics.
        public string PairingCode { get; }

        public string QuestClientId { get; }

        public string AppVersion { get; }

        public int MaximumMessageBytes { get; }

        public int MaximumTelemetryEventsPerBatch { get; }

        private static Uri ValidateEndpoint(Uri endpoint)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri)
            {
                throw new ArgumentException(
                    "An absolute relay WebSocket endpoint is required.",
                    nameof(endpoint));
            }

            if (!string.Equals(
                    endpoint.Scheme,
                    "ws",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    endpoint.Scheme,
                    "wss",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The relay endpoint must use ws or wss.",
                    nameof(endpoint));
            }

            return endpoint;
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty relay connection value is required.",
                    parameterName);
            }

            return value.Trim();
        }
    }
}
