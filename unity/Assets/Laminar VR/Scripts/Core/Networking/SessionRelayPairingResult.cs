using System;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class SessionRelayPairingResult
    {
        private SessionRelayPairingResult(
            string schemaVersion,
            string messageId,
            bool accepted,
            string sessionId,
            string rejectionCode)
        {
            SchemaVersion = RequireText(
                schemaVersion,
                nameof(schemaVersion));
            MessageId = RequireText(messageId, nameof(messageId));
            Accepted = accepted;
            if (accepted)
            {
                SessionId = RequireText(sessionId, nameof(sessionId));
                RejectionCode = null;
            }
            else
            {
                SessionId = null;
                RejectionCode = RequireText(
                    rejectionCode,
                    nameof(rejectionCode));
            }
        }

        public string SchemaVersion { get; }

        public string MessageId { get; }

        public bool Accepted { get; }

        public string SessionId { get; }

        // Must be a non-sensitive machine-readable reason.
        public string RejectionCode { get; }

        public static SessionRelayPairingResult Accept(
            string schemaVersion,
            string messageId,
            string sessionId)
        {
            return new SessionRelayPairingResult(
                schemaVersion,
                messageId,
                true,
                sessionId,
                null);
        }

        public static SessionRelayPairingResult Reject(
            string schemaVersion,
            string messageId,
            string rejectionCode)
        {
            return new SessionRelayPairingResult(
                schemaVersion,
                messageId,
                false,
                null,
                rejectionCode);
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty pairing result value is required.",
                    parameterName);
            }

            return value.Trim();
        }
    }
}
