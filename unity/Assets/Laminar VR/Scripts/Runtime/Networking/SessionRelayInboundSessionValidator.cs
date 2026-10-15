using System;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public static class SessionRelayInboundSessionValidator
    {
        public static bool TryValidate(
            string activeSessionId,
            string inboundSessionId,
            out SessionRelayInboundRejectionReason rejectionReason)
        {
            if (!string.IsNullOrWhiteSpace(activeSessionId)
                && string.Equals(
                    activeSessionId,
                    inboundSessionId,
                    StringComparison.Ordinal))
            {
                rejectionReason = default;
                return true;
            }

            rejectionReason =
                SessionRelayInboundRejectionReason.SessionMismatch;
            return false;
        }
    }
}
