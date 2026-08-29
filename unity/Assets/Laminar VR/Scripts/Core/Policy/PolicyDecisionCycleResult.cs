using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Safety;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public enum PolicyDecisionCycleResultCode
    {
        TransitionStarted,
        RewardWindowOpened,
        SafetyRejected,
        SkippedInvalidPhase,
        SkippedNetworkUnavailable,
        SkippedTransitionActive,
        SkippedRewardPending,
        SkippedPhysiologyUnavailable
    }

    public readonly struct PolicyDecisionCycleResult
    {
        internal PolicyDecisionCycleResult(
            PolicyDecisionCycleResultCode resultCode,
            PolicyDecision decision,
            ActionValidationResult? validation,
            PhysiologyQueryResultCode? physiologyQueryResult)
        {
            ResultCode = resultCode;
            Decision = decision;
            Validation = validation;
            PhysiologyQueryResult = physiologyQueryResult;
        }

        public PolicyDecisionCycleResultCode ResultCode { get; }

        public PolicyDecision Decision { get; }

        public ActionValidationResult? Validation { get; }

        public PhysiologyQueryResultCode? PhysiologyQueryResult { get; }
    }

    public enum PolicyRewardCycleResultCode
    {
        RewardApplied,
        RewardInvalid,
        AttributionInvalidated,
        Waiting,
        NoPending
    }

    public readonly struct PolicyRewardCycleResult
    {
        internal PolicyRewardCycleResult(
            PolicyRewardCycleResultCode resultCode,
            Rewards.RewardAttributionResolutionCode attributionCode,
            Rewards.RewardCalculationResult calculation)
        {
            ResultCode = resultCode;
            AttributionCode = attributionCode;
            Calculation = calculation;
        }

        public PolicyRewardCycleResultCode ResultCode { get; }

        public Rewards.RewardAttributionResolutionCode AttributionCode
        {
            get;
        }

        public Rewards.RewardCalculationResult Calculation { get; }
    }
}
