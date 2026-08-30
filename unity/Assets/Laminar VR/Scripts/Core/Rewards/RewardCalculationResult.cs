using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Rewards
{
    public enum RewardCalculationResultCode
    {
        Valid,
        InvalidPhysiologyWindow,
        NonIncreasingWindowSequence,
        OverlappingPhysiologyWindows,
        BaselineUnavailable,
        MissingRmssd,
        InvalidEnvironmentState,
        ActionEnvironmentMismatch,
        InvalidPenaltySeverity,
        NonFiniteReward
    }

    public readonly struct RewardBreakdown
    {
        internal RewardBreakdown(
            EnvironmentAction executedAction,
            long preWindowSequenceNumber,
            long postWindowSequenceNumber,
            double stressScoreImprovement,
            double rmssdMsImprovement,
            double heartRateBpmIncrease,
            double normalizedStressImprovement,
            double normalizedRmssdImprovement,
            double normalizedHeartRateIncrease,
            double actionMagnitude,
            double discomfortSeverity,
            double safetySeverity,
            double stressComponent,
            double rmssdComponent,
            double heartRateComponent,
            double changePenaltyComponent,
            double discomfortPenaltyComponent,
            double safetyPenaltyComponent,
            double totalReward)
        {
            ExecutedAction = executedAction;
            PreWindowSequenceNumber = preWindowSequenceNumber;
            PostWindowSequenceNumber = postWindowSequenceNumber;
            StressScoreImprovement = stressScoreImprovement;
            RmssdMsImprovement = rmssdMsImprovement;
            HeartRateBpmIncrease = heartRateBpmIncrease;
            NormalizedStressImprovement = normalizedStressImprovement;
            NormalizedRmssdImprovement = normalizedRmssdImprovement;
            NormalizedHeartRateIncrease = normalizedHeartRateIncrease;
            ActionMagnitude = actionMagnitude;
            DiscomfortSeverity = discomfortSeverity;
            SafetySeverity = safetySeverity;
            StressComponent = stressComponent;
            RmssdComponent = rmssdComponent;
            HeartRateComponent = heartRateComponent;
            ChangePenaltyComponent = changePenaltyComponent;
            DiscomfortPenaltyComponent = discomfortPenaltyComponent;
            SafetyPenaltyComponent = safetyPenaltyComponent;
            TotalReward = totalReward;
        }

        public EnvironmentAction ExecutedAction { get; }

        public long PreWindowSequenceNumber { get; }

        public long PostWindowSequenceNumber { get; }

        public double StressScoreImprovement { get; }

        public double RmssdMsImprovement { get; }

        public double HeartRateBpmIncrease { get; }

        public double NormalizedStressImprovement { get; }

        public double NormalizedRmssdImprovement { get; }

        public double NormalizedHeartRateIncrease { get; }

        public double ActionMagnitude { get; }

        public double DiscomfortSeverity { get; }

        public double SafetySeverity { get; }

        public double StressComponent { get; }

        public double RmssdComponent { get; }

        public double HeartRateComponent { get; }

        public double ChangePenaltyComponent { get; }

        public double DiscomfortPenaltyComponent { get; }

        public double SafetyPenaltyComponent { get; }

        public double TotalReward { get; }
    }

    public readonly struct RewardCalculationResult
    {
        internal RewardCalculationResult(
            RewardCalculationResultCode resultCode,
            RewardBreakdown breakdown)
        {
            ResultCode = resultCode;
            Breakdown = breakdown;
        }

        public RewardCalculationResultCode ResultCode { get; }

        public bool Valid => ResultCode == RewardCalculationResultCode.Valid;

        public RewardBreakdown Breakdown { get; }

        internal static RewardCalculationResult Invalid(
            RewardCalculationResultCode resultCode)
        {
            return new RewardCalculationResult(resultCode, default);
        }
    }
}
