using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Safety
{
    public enum ActionValidationReasonCode
    {
        Accepted = 0,
        RangeClipped = 1,
        SessionNotAdaptive = 2,
        SignalInvalid = 3,
        SignalStale = 4,
        CooldownActive = 5,
        SensitivityRestriction = 6,
        ConsecutiveDirectionLimit = 7,
        TotalVariationLimit = 8,
        Paused = 9,
        EmergencyStop = 10,
        TransitionActive = 11,
        Stabilization = 12,
        ParameterAtBoundary = 13,
        ConfigurationError = 14
    }

    public readonly struct ActionValidationResult
    {
        internal ActionValidationResult(
            EnvironmentAction proposedAction,
            EnvironmentAction executedAction,
            bool accepted,
            bool modified,
            ActionValidationReasonCode reasonCode,
            EnvironmentState requestedTarget,
            EnvironmentState safeTarget,
            double appliedVariation)
        {
            ProposedAction = proposedAction;
            ExecutedAction = executedAction;
            Accepted = accepted;
            Modified = modified;
            ReasonCode = reasonCode;
            RequestedTarget = requestedTarget;
            SafeTarget = safeTarget;
            AppliedVariation = appliedVariation;
        }

        public EnvironmentAction ProposedAction { get; }

        public EnvironmentAction ExecutedAction { get; }

        public bool Accepted { get; }

        public bool Modified { get; }

        public ActionValidationReasonCode ReasonCode { get; }

        public EnvironmentState RequestedTarget { get; }

        public EnvironmentState SafeTarget { get; }

        public double AppliedVariation { get; }
    }
}
