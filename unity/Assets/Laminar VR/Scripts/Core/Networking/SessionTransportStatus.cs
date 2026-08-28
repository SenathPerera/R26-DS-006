using System;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public enum SessionTransportConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting
    }

    public enum SessionTransportStatusReason
    {
        ConnectRequested,
        ConnectSucceeded,
        DisconnectRequested,
        DisconnectSucceeded,
        ConnectionLost,
        ConnectionFailed,
        OperationCancelled
    }

    public readonly struct SessionTransportStatus
    {
        public SessionTransportStatus(
            SessionTransportConnectionState previousState,
            SessionTransportConnectionState currentState,
            SessionTransportStatusReason reason,
            string diagnosticCode = null)
        {
            if (previousState == currentState)
            {
                throw new ArgumentException(
                    "A transport status change must move to a different state.");
            }

            if (diagnosticCode != null)
            {
                diagnosticCode = diagnosticCode.Trim();
                if (diagnosticCode.Length == 0)
                {
                    diagnosticCode = null;
                }
            }

            PreviousState = previousState;
            CurrentState = currentState;
            Reason = reason;
            DiagnosticCode = diagnosticCode;
        }

        public SessionTransportConnectionState PreviousState { get; }

        public SessionTransportConnectionState CurrentState { get; }

        public SessionTransportStatusReason Reason { get; }

        // Diagnostic codes must remain non-sensitive and transport-neutral.
        public string DiagnosticCode { get; }
    }
}
