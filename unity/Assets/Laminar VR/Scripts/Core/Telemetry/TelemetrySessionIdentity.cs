using System;

namespace LaminarVR.AdaptiveMeditation.Telemetry
{
    public sealed class TelemetrySessionIdentity
    {
        public TelemetrySessionIdentity(
            string sessionId,
            string participantPseudonym)
        {
            SessionId = RequireSafeText(sessionId, nameof(sessionId));
            ParticipantPseudonym = RequireSafeText(
                participantPseudonym,
                nameof(participantPseudonym));
        }

        public string SessionId { get; }

        // The caller owns pseudonymization. This boundary deliberately cannot
        // infer whether an arbitrary identifier contains personal information.
        public string ParticipantPseudonym { get; }

        private static string RequireSafeText(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty identifier is required.",
                    parameterName);
            }

            var trimmed = value.Trim();
            for (var index = 0; index < trimmed.Length; index++)
            {
                if (char.IsControl(trimmed[index]))
                {
                    throw new ArgumentException(
                        "Identifiers must not contain control characters.",
                        parameterName);
                }
            }

            return trimmed;
        }
    }
}
