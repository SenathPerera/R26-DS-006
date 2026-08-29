using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    public sealed class EnvironmentParameterManager
        : IEnvironmentParameterManager
    {
        private readonly ISceneEnvironmentAdapter sceneAdapter;

        private string activeTransitionId;
        private EnvironmentState transitionStartState;
        private EnvironmentState transitionTargetState;
        private double transitionStartMonotonicTimeSeconds;
        private double transitionDurationSeconds;

        public EnvironmentParameterManager(
            EnvironmentState initialState,
            ISceneEnvironmentAdapter sceneAdapter)
        {
            if (!initialState.IsNormalized)
            {
                throw new ArgumentException(
                    "The initial environment state must be normalized.",
                    nameof(initialState));
            }

            this.sceneAdapter = sceneAdapter
                ?? throw new ArgumentNullException(nameof(sceneAdapter));
            CurrentState = initialState;
            TargetState = initialState;
            sceneAdapter.ApplyState(initialState);
        }

        public EnvironmentState CurrentState { get; private set; }

        public EnvironmentState TargetState { get; private set; }

        public bool IsTransitionActive => activeTransitionId != null;

        public string ActiveTransitionId => activeTransitionId;

        public void BeginTransition(
            string transitionId,
            EnvironmentState targetState,
            double startMonotonicTimeSeconds,
            double durationSeconds)
        {
            if (IsTransitionActive)
            {
                throw new InvalidOperationException(
                    "An environment transition is already active.");
            }

            if (string.IsNullOrWhiteSpace(transitionId))
            {
                throw new ArgumentException(
                    "A transition ID is required.",
                    nameof(transitionId));
            }

            if (!targetState.IsNormalized)
            {
                throw new ArgumentException(
                    "The transition target must be normalized.",
                    nameof(targetState));
            }

            ValidateFiniteNonNegative(
                startMonotonicTimeSeconds,
                nameof(startMonotonicTimeSeconds));
            if (!IsFinite(durationSeconds) || durationSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds),
                    durationSeconds,
                    "Transition duration must be finite and positive.");
            }

            activeTransitionId = transitionId.Trim();
            transitionStartState = CurrentState;
            transitionTargetState = targetState;
            TargetState = targetState;
            transitionStartMonotonicTimeSeconds =
                startMonotonicTimeSeconds;
            transitionDurationSeconds = durationSeconds;
        }

        public EnvironmentTransitionProgress AdvanceTransition(
            double currentMonotonicTimeSeconds)
        {
            ValidateFiniteNonNegative(
                currentMonotonicTimeSeconds,
                nameof(currentMonotonicTimeSeconds));
            if (!IsTransitionActive)
            {
                return EnvironmentTransitionProgress.Idle(CurrentState);
            }

            if (currentMonotonicTimeSeconds
                < transitionStartMonotonicTimeSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentMonotonicTimeSeconds),
                    currentMonotonicTimeSeconds,
                    "Transition time cannot precede its start time.");
            }

            var normalizedProgress = Math.Min(
                1d,
                (currentMonotonicTimeSeconds
                    - transitionStartMonotonicTimeSeconds)
                / transitionDurationSeconds);
            CurrentState = Interpolate(
                transitionStartState,
                transitionTargetState,
                (float)normalizedProgress);
            sceneAdapter.ApplyState(CurrentState);

            var transitionId = activeTransitionId;
            if (normalizedProgress < 1d)
            {
                return new EnvironmentTransitionProgress(
                    EnvironmentTransitionStatus.InProgress,
                    transitionId,
                    CurrentState,
                    normalizedProgress,
                    null);
            }

            var completedAt = transitionStartMonotonicTimeSeconds
                + transitionDurationSeconds;
            activeTransitionId = null;
            TargetState = CurrentState;
            return new EnvironmentTransitionProgress(
                EnvironmentTransitionStatus.Completed,
                transitionId,
                CurrentState,
                1d,
                completedAt);
        }

        public bool CancelTransition(out string cancelledTransitionId)
        {
            if (!IsTransitionActive)
            {
                cancelledTransitionId = null;
                return false;
            }

            cancelledTransitionId = activeTransitionId;
            activeTransitionId = null;
            TargetState = CurrentState;
            return true;
        }

        private static EnvironmentState Interpolate(
            EnvironmentState start,
            EnvironmentState target,
            float progress)
        {
            return new EnvironmentState(
                Lerp(start.Illumination, target.Illumination, progress),
                Lerp(start.Warmth, target.Warmth, progress),
                Lerp(
                    start.AtmosphericSoftness,
                    target.AtmosphericSoftness,
                    progress),
                Lerp(start.ColorRichness, target.ColorRichness, progress),
                Lerp(start.AmbientMotion, target.AmbientMotion, progress));
        }

        private static float Lerp(float start, float target, float progress)
        {
            return start + ((target - start) * progress);
        }

        private static void ValidateFiniteNonNegative(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
