using System;
using LaminarVR.AdaptiveMeditation.Session;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class SessionRelayQuestState
    {
        public SessionRelayQuestState(
            string messageId,
            string sessionId,
            VrSessionPhase phase,
            double utcTimestampUnixSeconds)
        {
            MessageId = RequireText(messageId, nameof(messageId));
            SessionId = RequireText(sessionId, nameof(sessionId));
            if (!Enum.IsDefined(typeof(VrSessionPhase), phase))
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            if (double.IsNaN(utcTimestampUnixSeconds)
                || double.IsInfinity(utcTimestampUnixSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(utcTimestampUnixSeconds));
            }

            Phase = phase;
            UtcTimestampUnixSeconds = utcTimestampUnixSeconds;
        }

        public string MessageId { get; }

        public string SessionId { get; }

        public VrSessionPhase Phase { get; }

        public double UtcTimestampUnixSeconds { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty Quest-state value is required.",
                    parameterName);
            }

            return value.Trim();
        }
    }
}
