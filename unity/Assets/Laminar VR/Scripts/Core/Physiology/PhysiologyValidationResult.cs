namespace LaminarVR.AdaptiveMeditation.Physiology
{
    public enum PhysiologyValidationReasonCode
    {
        Accepted,
        PayloadMissing,
        ReceiptTimestampInvalid,
        TimestampInvalid,
        SourceTimestampMismatch,
        WindowOrderInvalid,
        WindowTooShort,
        FutureTimestamp,
        StaleAtReceipt,
        HeartRateInvalid,
        RmssdInvalid,
        SdnnInvalid,
        SignalQualityInvalid,
        StressDecisionMissing,
        StressModeInvalid,
        StressLevelsInvalid,
        StressLabelMissing,
        StressConfidenceInvalid,
        StressProbabilitiesInvalid,
        StressProbabilitySumInvalid,
        ContinuousStressScoreInvalid
    }

    public readonly struct PhysiologyValidationResult
    {
        public PhysiologyValidationResult(
            bool accepted,
            PhysiologyValidationReasonCode reasonCode)
        {
            Accepted = accepted;
            ReasonCode = reasonCode;
        }

        public bool Accepted { get; }

        public PhysiologyValidationReasonCode ReasonCode { get; }

        public static PhysiologyValidationResult Valid =>
            new PhysiologyValidationResult(
                true,
                PhysiologyValidationReasonCode.Accepted);

        public static PhysiologyValidationResult Reject(
            PhysiologyValidationReasonCode reasonCode)
        {
            return new PhysiologyValidationResult(false, reasonCode);
        }
    }
}

