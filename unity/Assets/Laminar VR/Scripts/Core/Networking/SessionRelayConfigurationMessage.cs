using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class SessionRelayConfigurationMessage
    {
        public SessionRelayConfigurationMessage(
            string schemaVersion,
            string messageId,
            string sessionId,
            string participantPseudonym,
            string sceneId,
            EnvironmentState preferredEnvironment)
        {
            SchemaVersion = RequireIdentifier(
                schemaVersion,
                nameof(schemaVersion));
            MessageId = RequireIdentifier(messageId, nameof(messageId));
            SessionId = RequireIdentifier(sessionId, nameof(sessionId));
            ParticipantPseudonym = RequireIdentifier(
                participantPseudonym,
                nameof(participantPseudonym));
            SceneId = RequireIdentifier(sceneId, nameof(sceneId));
            if (!preferredEnvironment.IsNormalized)
            {
                throw new ArgumentException(
                    "Relay preferences must use normalized [0, 1] values.",
                    nameof(preferredEnvironment));
            }

            PreferredEnvironment = preferredEnvironment;
        }

        public string SchemaVersion { get; }

        public string MessageId { get; }

        public string SessionId { get; }

        public string ParticipantPseudonym { get; }

        public string SceneId { get; }

        public EnvironmentState PreferredEnvironment { get; }

        private static string RequireIdentifier(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty relay identifier is required.",
                    parameterName);
            }

            return value.Trim();
        }
    }
}
