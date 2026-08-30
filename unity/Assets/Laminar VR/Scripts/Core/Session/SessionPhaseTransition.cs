namespace LaminarVR.AdaptiveMeditation.Session
{
    public enum SessionTransitionReason
    {
        BootCompleted,
        ConfigurationAccepted,
        SceneLoaded,
        StartCommand,
        AcclimatizationElapsed,
        PauseCommand,
        ResumeCommand,
        AdaptiveDurationElapsed,
        StabilizationDurationElapsed,
        StopCommand,
        EmergencyStopCommand,
        FatalError
    }

    public readonly struct SessionPhaseTransition
    {
        public SessionPhaseTransition(
            VrSessionPhase previousPhase,
            VrSessionPhase currentPhase,
            SessionTransitionReason reason,
            double monotonicTimeSeconds,
            double activeSessionElapsedSeconds)
        {
            PreviousPhase = previousPhase;
            CurrentPhase = currentPhase;
            Reason = reason;
            MonotonicTimeSeconds = monotonicTimeSeconds;
            ActiveSessionElapsedSeconds = activeSessionElapsedSeconds;
        }

        public VrSessionPhase PreviousPhase { get; }

        public VrSessionPhase CurrentPhase { get; }

        public SessionTransitionReason Reason { get; }

        public double MonotonicTimeSeconds { get; }

        public double ActiveSessionElapsedSeconds { get; }
    }
}

