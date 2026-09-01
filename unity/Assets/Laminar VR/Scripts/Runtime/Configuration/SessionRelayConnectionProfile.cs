using System;
using LaminarVR.AdaptiveMeditation.Networking;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "SessionRelayConnectionProfile",
        menuName = "Adaptive Meditation/Networking/Session Relay Profile")]
    public sealed class SessionRelayConnectionProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after the relay endpoint, schema, and message limit "
            + "are valid for the active deployment environment.")]
        [SerializeField]
        private bool deploymentConfigurationApproved = false;

        [Header("Session Relay")]
        [Tooltip(
            "Use wss:// in deployed builds. ws:// is accepted only when the "
            + "development override below is explicitly enabled.")]
        [SerializeField]
        private string relayEndpoint = string.Empty;

        [Tooltip(
            "Cross-component relay schema identifier agreed with the mobile "
            + "and relay implementations.")]
        [SerializeField]
        private string schemaVersion = string.Empty;

        [SerializeField, Min(0)]
        private int maximumMessageBytes = 0;

        [Tooltip(
            "Maximum recorded visual telemetry events sent in one relay "
            + "message. This limit must match relay deployment constraints.")]
        [SerializeField, Min(0)]
        private int maximumTelemetryEventsPerBatch = 0;

        [Tooltip(
            "Development-only opt-in for a non-TLS ws:// relay. Leave disabled "
            + "for deployed Quest builds.")]
        [SerializeField]
        private bool allowInsecureDevelopmentEndpoint = false;

        public string ConfigurationId => configurationId;

        public int ConfigurationVersion => configurationVersion;

        public bool TryCreateConnectionInfo(
            string pairingCode,
            string questClientId,
            string appVersion,
            out SessionRelayConnectionInfo connectionInfo,
            out string validationError)
        {
            connectionInfo = null;
            if (!deploymentConfigurationApproved)
            {
                validationError =
                    "Session relay deployment configuration is not approved.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(configurationId)
                || configurationVersion < 1)
            {
                validationError =
                    "Session relay configuration identity and version are required.";
                return false;
            }

            if (!Uri.TryCreate(
                    relayEndpoint?.Trim(),
                    UriKind.Absolute,
                    out var endpoint))
            {
                validationError =
                    "An absolute session relay endpoint is required.";
                return false;
            }

            if (string.Equals(
                    endpoint.Scheme,
                    "ws",
                    StringComparison.OrdinalIgnoreCase)
                && !allowInsecureDevelopmentEndpoint)
            {
                validationError =
                    "A ws:// relay requires the explicit development-only override.";
                return false;
            }

            try
            {
                connectionInfo = new SessionRelayConnectionInfo(
                    endpoint,
                    schemaVersion,
                    pairingCode,
                    questClientId,
                    appVersion,
                    maximumMessageBytes,
                    maximumTelemetryEventsPerBatch);
                validationError = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                validationError = exception.Message;
                return false;
            }
        }
    }
}
