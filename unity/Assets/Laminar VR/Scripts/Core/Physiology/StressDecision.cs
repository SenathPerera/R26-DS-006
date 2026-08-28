namespace LaminarVR.AdaptiveMeditation.Physiology
{
    // Component B's point/band schema and level names now align with the
    // blueprint. Unity preserves the producer's authoritative mode and label.
    public enum StressDecisionMode
    {
        Point,
        Band
    }

    public readonly struct StressProbabilityVector
    {
        public StressProbabilityVector(
            double level0Probability,
            double level1Probability,
            double level2Probability,
            double level3Probability)
        {
            Level0Probability = level0Probability;
            Level1Probability = level1Probability;
            Level2Probability = level2Probability;
            Level3Probability = level3Probability;
        }

        public double Level0Probability { get; }

        public double Level1Probability { get; }

        public double Level2Probability { get; }

        public double Level3Probability { get; }

        public double Sum =>
            Level0Probability
            + Level1Probability
            + Level2Probability
            + Level3Probability;

        public bool IsFiniteAndInUnitRange =>
            IsFiniteAndInUnitRangeValue(Level0Probability)
            && IsFiniteAndInUnitRangeValue(Level1Probability)
            && IsFiniteAndInUnitRangeValue(Level2Probability)
            && IsFiniteAndInUnitRangeValue(Level3Probability);

        private static bool IsFiniteAndInUnitRangeValue(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= 0d
                && value <= 1d;
        }
    }

    public sealed class StressDecision
    {
        public StressDecision(
            StressDecisionMode mode,
            int? pointLevel,
            int? bandLowLevel,
            int? bandHighLevel,
            string label,
            double confidence,
            bool? adjacent,
            StressProbabilityVector probabilities,
            double continuousScore)
        {
            Mode = mode;
            PointLevel = pointLevel;
            BandLowLevel = bandLowLevel;
            BandHighLevel = bandHighLevel;
            Label = label;
            Confidence = confidence;
            Adjacent = adjacent;
            Probabilities = probabilities;
            ContinuousScore = continuousScore;
        }

        public StressDecisionMode Mode { get; }

        public int? PointLevel { get; }

        public int? BandLowLevel { get; }

        public int? BandHighLevel { get; }

        public string Label { get; }

        public double Confidence { get; }

        public bool? Adjacent { get; }

        public StressProbabilityVector Probabilities { get; }

        public double ContinuousScore { get; }
    }
}
