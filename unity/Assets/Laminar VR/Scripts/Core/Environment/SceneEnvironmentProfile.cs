using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    public sealed class SceneEnvironmentProfile
    {
        public SceneEnvironmentProfile(
            string sceneId,
            string displayName,
            EnvironmentState safeDefault,
            EnvironmentStateLimits limits,
            float actionStep,
            float transitionDurationSeconds,
            float minimumSecondsBetweenActions)
        {
            if (string.IsNullOrWhiteSpace(sceneId))
            {
                throw new ArgumentException("Scene ID is required.", nameof(sceneId));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Display name is required.", nameof(displayName));
            }

            if (!safeDefault.IsNormalized)
            {
                throw new ArgumentException(
                    "The safe default environment state must be normalized.",
                    nameof(safeDefault));
            }

            if (!limits.Contains(safeDefault))
            {
                throw new ArgumentException(
                    "The safe default environment state must be within the scene limits.",
                    nameof(safeDefault));
            }

            ValidateActionStep(actionStep);
            ValidatePositiveDuration(
                transitionDurationSeconds,
                nameof(transitionDurationSeconds));
            ValidateNonNegativeDuration(
                minimumSecondsBetweenActions,
                nameof(minimumSecondsBetweenActions));

            SceneId = sceneId.Trim();
            DisplayName = displayName.Trim();
            SafeDefault = safeDefault;
            Limits = limits;
            ActionStep = actionStep;
            TransitionDurationSeconds = transitionDurationSeconds;
            MinimumSecondsBetweenActions = minimumSecondsBetweenActions;
        }

        public string SceneId { get; }

        public string DisplayName { get; }

        public EnvironmentState SafeDefault { get; }

        public EnvironmentStateLimits Limits { get; }

        public float ActionStep { get; }

        public float TransitionDurationSeconds { get; }

        public float MinimumSecondsBetweenActions { get; }

        private static void ValidateActionStep(float actionStep)
        {
            if (!IsFinite(actionStep) || actionStep <= 0f || actionStep > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(actionStep),
                    actionStep,
                    "Action step must be finite and greater than 0 and no greater than 1.");
            }
        }

        private static void ValidatePositiveDuration(float durationSeconds, string parameterName)
        {
            if (!IsFinite(durationSeconds) || durationSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    durationSeconds,
                    "Duration must be finite and greater than 0 seconds.");
            }
        }

        private static void ValidateNonNegativeDuration(
            float durationSeconds,
            string parameterName)
        {
            if (!IsFinite(durationSeconds) || durationSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    durationSeconds,
                    "Duration must be finite and non-negative.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
