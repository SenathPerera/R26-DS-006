namespace LaminarVR.AdaptiveMeditation.Physiology
{
    public enum PhysiologyIngestionResultCode
    {
        Accepted,
        PayloadRejected,
        DuplicateWindow,
        OutOfOrderWindow,
        InvalidReceiptTime,
        NonMonotonicReceiptTime
    }

    public readonly struct PhysiologyIngestionResult
    {
        public PhysiologyIngestionResult(
            PhysiologyIngestionResultCode resultCode,
            PhysiologyValidationReasonCode validationReasonCode,
            long acceptedSequenceNumber)
        {
            ResultCode = resultCode;
            ValidationReasonCode = validationReasonCode;
            AcceptedSequenceNumber = acceptedSequenceNumber;
        }

        public PhysiologyIngestionResultCode ResultCode { get; }

        public PhysiologyValidationReasonCode ValidationReasonCode { get; }

        public long AcceptedSequenceNumber { get; }

        public bool Accepted => ResultCode == PhysiologyIngestionResultCode.Accepted;
    }

    public enum PhysiologyDataUse
    {
        Display,
        Decision,
        Resume,
        Reward
    }

    public enum PhysiologyQueryResultCode
    {
        Available,
        NoData,
        NoNewWindow,
        InvalidQueryTime,
        Stale,
        InsufficientSignalQuality,
        UnsupportedUse
    }

    public readonly struct PhysiologyWindowSnapshot
    {
        public PhysiologyWindowSnapshot(
            long sequenceNumber,
            PhysiologyWindow window,
            double ageSeconds)
            : this(sequenceNumber, window, ageSeconds, double.NaN)
        {
        }

        public PhysiologyWindowSnapshot(
            long sequenceNumber,
            PhysiologyWindow window,
            double ageSeconds,
            double receivedMonotonicTimeSeconds)
        {
            SequenceNumber = sequenceNumber;
            Window = window;
            AgeSeconds = ageSeconds;
            ReceivedMonotonicTimeSeconds = receivedMonotonicTimeSeconds;
        }

        public long SequenceNumber { get; }

        public PhysiologyWindow Window { get; }

        public double AgeSeconds { get; }

        public double ReceivedMonotonicTimeSeconds { get; }
    }
}
