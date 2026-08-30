using System;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public readonly struct ReconnectAttemptResult
    {
        internal ReconnectAttemptResult(
            bool connected,
            int attemptsMade,
            Exception lastFailure)
        {
            Connected = connected;
            AttemptsMade = attemptsMade;
            LastFailure = lastFailure;
        }

        public bool Connected { get; }

        public int AttemptsMade { get; }

        public Exception LastFailure { get; }

        public bool Exhausted => !Connected && AttemptsMade > 0;
    }
}
