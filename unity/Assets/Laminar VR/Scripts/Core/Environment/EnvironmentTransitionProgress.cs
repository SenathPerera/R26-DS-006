using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    public enum EnvironmentTransitionStatus
    {
        Idle,
        InProgress,
        Completed
    }

    public readonly struct EnvironmentTransitionProgress
    {
        public EnvironmentTransitionProgress(
            EnvironmentTransitionStatus status,
            string transitionId,
            EnvironmentState state,
            double normalizedProgress,
            double? completedMonotonicTimeSeconds)
        {
            if (!Enum.IsDefined(typeof(EnvironmentTransitionStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            if (!state.IsNormalized)
            {
                throw new ArgumentException(
                    "Transition state must be normalized.",
                    nameof(state));
            }

            if (double.IsNaN(normalizedProgress)
                || double.IsInfinity(normalizedProgress)
                || normalizedProgress < 0d
                || normalizedProgress > 1d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(normalizedProgress));
            }

            if (status == EnvironmentTransitionStatus.Idle)
            {
                if (transitionId != null
                    || normalizedProgress != 0d
                    || completedMonotonicTimeSeconds.HasValue)
                {
                    throw new ArgumentException(
                        "Idle transition progress cannot carry active data.");
                }
            }
            else if (string.IsNullOrWhiteSpace(transitionId))
            {
                throw new ArgumentException(
                    "A transition ID is required for active progress.",
                    nameof(transitionId));
            }

            if (status == EnvironmentTransitionStatus.Completed
                && (!completedMonotonicTimeSeconds.HasValue
                    || !IsFiniteNonNegative(
                        completedMonotonicTimeSeconds.Value)))
            {
                throw new ArgumentException(
                    "Completed progress requires a valid completion time.",
                    nameof(completedMonotonicTimeSeconds));
            }

            Status = status;
            TransitionId = transitionId;
            State = state;
            NormalizedProgress = normalizedProgress;
            CompletedMonotonicTimeSeconds = completedMonotonicTimeSeconds;
        }

        public EnvironmentTransitionStatus Status { get; }

        public string TransitionId { get; }

        public EnvironmentState State { get; }

        public double NormalizedProgress { get; }

        public double? CompletedMonotonicTimeSeconds { get; }

        public static EnvironmentTransitionProgress Idle(
            EnvironmentState state)
        {
            return new EnvironmentTransitionProgress(
                EnvironmentTransitionStatus.Idle,
                null,
                state,
                0d,
                null);
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= 0d;
        }
    }
}
