using System;
using LaminarVR.AdaptiveMeditation.Session;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class SessionRelayCommandMessage
    {
        public SessionRelayCommandMessage(
            string schemaVersion,
            string messageId,
            string sessionId,
            SessionCommandType commandType)
        {
            SchemaVersion = RequireIdentifier(
                schemaVersion,
                nameof(schemaVersion));
            MessageId = RequireIdentifier(messageId, nameof(messageId));
            SessionId = RequireIdentifier(sessionId, nameof(sessionId));
            if (!Enum.IsDefined(typeof(SessionCommandType), commandType))
            {
                throw new ArgumentOutOfRangeException(nameof(commandType));
            }

            CommandType = commandType;
        }

        public string SchemaVersion { get; }

        // The stable relay message ID is also the coordinator command ID.
        public string MessageId { get; }

        public string SessionId { get; }

        public SessionCommandType CommandType { get; }

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
