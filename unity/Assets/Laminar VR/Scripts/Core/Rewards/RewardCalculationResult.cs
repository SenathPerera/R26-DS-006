using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Safety;

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
        NonFiniteReward,
        MissingDiscomfortSeverity
    }

    public sealed class RewardPenaltyObservation
    {
        private RewardPenaltyObservation(
            double? discomfortSeverity,
            double safetySeverity,
            bool safetyInterventionOccurred)
        {
            DiscomfortSeverity = discomfortSeverity;
            SafetySeverity = safetySeverity;
            SafetyInterventionOccurred = safetyInterventionOccurred;
        }

        public double? DiscomfortSeverity { get; }

        public double SafetySeverity { get; }

        public bool SafetyInterventionOccurred { get; }

        public static RewardPenaltyObservation FromActionValidation(
            ActionValidationResult validation)
        {
            if (!validation.Accepted)
            {
                throw new System.ArgumentException(
                    "Reward penalties require an accepted safety validation.",
                    nameof(validation));
            }

            // The MVP has one deterministic safety-severity level: an
            // accepted action modified by the safety layer is a full
            // intervention; an unmodified action is not an intervention.
            var safetyInterventionOccurred = validation.Modified;
            return new RewardPenaltyObservation(
                null,
                safetyInterventionOccurred ? 1d : 0d,
                safetyInterventionOccurred);
        }

        internal static RewardPenaltyObservation CreateForTesting(
            double? discomfortSeverity,
            double safetySeverity,
            bool safetyInterventionOccurred)
        {
            return new RewardPenaltyObservation(
                discomfortSeverity,
                safetySeverity,
                safetyInterventionOccurred);
        }
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
            double? discomfortSeverity,
            double safetySeverity,
            bool safetyInterventionOccurred,
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
            SafetyInterventionOccurred = safetyInterventionOccurred;
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

        public double? DiscomfortSeverity { get; }

        public double SafetySeverity { get; }

        public bool SafetyInterventionOccurred { get; }

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
