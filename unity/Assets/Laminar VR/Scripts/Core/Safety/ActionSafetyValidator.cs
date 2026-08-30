using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Safety
{
    public sealed class ActionSafetyValidator : IActionSafetyValidator
    {
        private const double VariationComparisonTolerance = 1e-9d;

        public ActionValidationResult Validate(
            EnvironmentAction proposedAction,
            EnvironmentState currentState,
            SceneEnvironmentProfile sceneProfile,
            SafetyRuntimeState runtimeState,
            ActionSafetyLimits safetyLimits)
        {
            if (sceneProfile == null)
            {
                throw new ArgumentNullException(nameof(sceneProfile));
            }

            if (!IsSupportedAction(proposedAction))
            {
                return Reject(
                    proposedAction,
                    ActionValidationReasonCode.ConfigurationError,
                    currentState,
                    currentState);
            }

            if (!currentState.IsNormalized || !sceneProfile.Limits.Contains(currentState))
            {
                return Reject(
                    proposedAction,
                    ActionValidationReasonCode.ConfigurationError,
                    currentState,
                    currentState);
            }

            if (runtimeState.BlockReason != SafetyBlockReason.None)
            {
                return Reject(
                    proposedAction,
                    MapBlockReason(runtimeState.BlockReason),
                    currentState,
                    currentState);
            }

            if (proposedAction == EnvironmentAction.NoChange)
            {
                return new ActionValidationResult(
                    proposedAction,
                    EnvironmentAction.NoChange,
                    true,
                    false,
                    ActionValidationReasonCode.Accepted,
                    currentState,
                    currentState,
                    0d);
            }

            if (ViolatesConsecutiveDirectionLimit(
                    proposedAction,
                    runtimeState,
                    safetyLimits))
            {
                return Reject(
                    proposedAction,
                    ActionValidationReasonCode.ConsecutiveDirectionLimit,
                    currentState,
                    currentState);
            }

            var requestedTarget = EnvironmentActionApplier.Apply(
                currentState,
                proposedAction,
                sceneProfile.ActionSteps);
            var safeTarget = sceneProfile.Limits.Clamp(requestedTarget);

            if (safeTarget == currentState)
            {
                return Reject(
                    proposedAction,
                    ActionValidationReasonCode.ParameterAtBoundary,
                    requestedTarget,
                    currentState);
            }

            var appliedVariation = currentState.L1DistanceTo(safeTarget);
            if ((runtimeState.TotalVariation + appliedVariation)
                - safetyLimits.MaximumTotalVariation
                > VariationComparisonTolerance)
            {
                return Reject(
                    proposedAction,
                    ActionValidationReasonCode.TotalVariationLimit,
                    requestedTarget,
                    currentState);
            }

            var wasClipped = requestedTarget != safeTarget;
            return new ActionValidationResult(
                proposedAction,
                proposedAction,
                true,
                wasClipped,
                wasClipped
                    ? ActionValidationReasonCode.RangeClipped
                    : ActionValidationReasonCode.Accepted,
                requestedTarget,
                safeTarget,
                appliedVariation);
        }

        private static ActionValidationResult Reject(
            EnvironmentAction proposedAction,
            ActionValidationReasonCode reasonCode,
            EnvironmentState requestedTarget,
            EnvironmentState safeTarget)
        {
            return new ActionValidationResult(
                proposedAction,
                EnvironmentAction.NoChange,
                false,
                proposedAction != EnvironmentAction.NoChange
                    || requestedTarget != safeTarget,
                reasonCode,
                requestedTarget,
                safeTarget,
                0d);
        }

        private static bool ViolatesConsecutiveDirectionLimit(
            EnvironmentAction proposedAction,
            SafetyRuntimeState runtimeState,
            ActionSafetyLimits safetyLimits)
        {
            return runtimeState.PreviousExecutedAction.HasValue
                && runtimeState.PreviousExecutedAction.Value == proposedAction
                && runtimeState.ConsecutiveSameDirectionActions
                    >= safetyLimits.MaximumConsecutiveSameDirectionActions;
        }

        private static bool IsSupportedAction(EnvironmentAction action)
        {
            var actionValue = (int)action;
            return actionValue >= (int)EnvironmentAction.NoChange
                && actionValue <= (int)EnvironmentAction.DecreaseAmbientMotion;
        }

        private static ActionValidationReasonCode MapBlockReason(
            SafetyBlockReason blockReason)
        {
            switch (blockReason)
            {
                case SafetyBlockReason.SessionNotAdaptive:
                    return ActionValidationReasonCode.SessionNotAdaptive;
                case SafetyBlockReason.SignalInvalid:
                    return ActionValidationReasonCode.SignalInvalid;
                case SafetyBlockReason.SignalStale:
                    return ActionValidationReasonCode.SignalStale;
                case SafetyBlockReason.CooldownActive:
                    return ActionValidationReasonCode.CooldownActive;
                case SafetyBlockReason.SensitivityRestriction:
                    return ActionValidationReasonCode.SensitivityRestriction;
                case SafetyBlockReason.Paused:
                    return ActionValidationReasonCode.Paused;
                case SafetyBlockReason.EmergencyStop:
                    return ActionValidationReasonCode.EmergencyStop;
                case SafetyBlockReason.TransitionActive:
                    return ActionValidationReasonCode.TransitionActive;
                case SafetyBlockReason.Stabilization:
                    return ActionValidationReasonCode.Stabilization;
                case SafetyBlockReason.ConfigurationError:
                    return ActionValidationReasonCode.ConfigurationError;
                case SafetyBlockReason.None:
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(blockReason),
                        blockReason,
                        "A non-empty safety block reason was expected.");
            }
        }
    }
}
