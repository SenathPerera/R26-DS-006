namespace LaminarVR.AdaptiveMeditation.Session
{
    public readonly struct SessionDecisionOpportunity
    {
        public SessionDecisionOpportunity(
            int sequenceNumber,
            double monotonicTimeSeconds,
            double adaptiveElapsedSeconds)
        {
            SequenceNumber = sequenceNumber;
            MonotonicTimeSeconds = monotonicTimeSeconds;
            AdaptiveElapsedSeconds = adaptiveElapsedSeconds;
        }

        public int SequenceNumber { get; }

        public double MonotonicTimeSeconds { get; }

        public double AdaptiveElapsedSeconds { get; }
    }
}

