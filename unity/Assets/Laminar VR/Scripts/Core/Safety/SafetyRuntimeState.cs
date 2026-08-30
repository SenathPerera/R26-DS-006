using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Safety
{
    public enum SafetyBlockReason
    {
        None = 0,
        SessionNotAdaptive = 1,
        SignalInvalid = 2,
        SignalStale = 3,
        CooldownActive = 4,
        SensitivityRestriction = 5,
        Paused = 6,
        EmergencyStop = 7,
        TransitionActive = 8,
        Stabilization = 9,
        ConfigurationError = 10
    }

    public readonly struct SafetyRuntimeState
    {
        public SafetyRuntimeState(
            SafetyBlockReason blockReason,
            EnvironmentAction? previousExecutedAction,
            int consecutiveSameDirectionActions,
            double totalVariation)
        {
            ValidateBlockReason(blockReason);
            ValidatePreviousAction(previousExecutedAction);

            if (consecutiveSameDirectionActions < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(consecutiveSameDirectionActions),
                    consecutiveSameDirectionActions,
                    "Consecutive-action count must be non-negative.");
            }

            if (!previousExecutedAction.HasValue && consecutiveSameDirectionActions != 0)
            {
                throw new ArgumentException(
                    "Consecutive-action count must be zero when there is no previous action.",
                    nameof(consecutiveSameDirectionActions));
            }

            if (double.IsNaN(totalVariation)
                || double.IsInfinity(totalVariation)
                || totalVariation < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalVariation),
                    totalVariation,
                    "Total variation must be finite and non-negative.");
            }

            BlockReason = blockReason;
            PreviousExecutedAction = previousExecutedAction;
            ConsecutiveSameDirectionActions = consecutiveSameDirectionActions;
            TotalVariation = totalVariation;
        }

        public SafetyBlockReason BlockReason { get; }

        public EnvironmentAction? PreviousExecutedAction { get; }

        public int ConsecutiveSameDirectionActions { get; }

        public double TotalVariation { get; }

        public static SafetyRuntimeState Ready =>
            new SafetyRuntimeState(SafetyBlockReason.None, null, 0, 0d);

        private static void ValidateBlockReason(SafetyBlockReason blockReason)
        {
            var reasonValue = (int)blockReason;
            if (reasonValue < (int)SafetyBlockReason.None
                || reasonValue > (int)SafetyBlockReason.ConfigurationError)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(blockReason),
                    blockReason,
                    "The safety block reason is not supported.");
            }
        }

        private static void ValidatePreviousAction(EnvironmentAction? previousAction)
        {
            if (!previousAction.HasValue)
            {
                return;
            }

            var actionValue = (int)previousAction.Value;
            if (actionValue < (int)EnvironmentAction.NoChange
                || actionValue > (int)EnvironmentAction.DecreaseAmbientMotion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(previousAction),
                    previousAction,
                    "The previous environment action is not supported.");
            }
        }
    }
}
