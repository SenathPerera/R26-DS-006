namespace LaminarVR.AdaptiveMeditation.Physiology
{
    // Transport-neutral validated domain object. Session/schema envelope
    // validation belongs in the future shared-contract transport adapter.
    // TODO(RESEARCH_DECISION): Finalize that shared envelope before networking.
    public sealed class PhysiologyWindow
    {
        public PhysiologyWindow(
            double sourceTimestampUtcUnixSeconds,
            double windowStartUtcUnixSeconds,
            double windowEndUtcUnixSeconds,
            double heartRateBpm,
            double? rmssdMs,
            double? sdnnMs,
            StressDecision stress,
            double signalQuality)
        {
            SourceTimestampUtcUnixSeconds = sourceTimestampUtcUnixSeconds;
            WindowStartUtcUnixSeconds = windowStartUtcUnixSeconds;
            WindowEndUtcUnixSeconds = windowEndUtcUnixSeconds;
            HeartRateBpm = heartRateBpm;
            RmssdMs = rmssdMs;
            SdnnMs = sdnnMs;
            Stress = stress;
            SignalQuality = signalQuality;
        }

        public double SourceTimestampUtcUnixSeconds { get; }

        public double WindowStartUtcUnixSeconds { get; }

        public double WindowEndUtcUnixSeconds { get; }

        public double HeartRateBpm { get; }

        public double? RmssdMs { get; }

        public double? SdnnMs { get; }

        public StressDecision Stress { get; }

        public double SignalQuality { get; }

        public double WindowDurationSeconds =>
            WindowEndUtcUnixSeconds - WindowStartUtcUnixSeconds;
    }
}
