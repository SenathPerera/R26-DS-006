using System;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public static class SyntheticStressDecisionFactory
    {
        private static readonly string[] Labels =
        {
            "relaxed",
            "mild",
            "moderate",
            "high"
        };

        public static StressDecision FromContinuousScore(double score)
        {
            if (double.IsNaN(score) || double.IsInfinity(score))
            {
                throw new ArgumentOutOfRangeException(nameof(score));
            }

            var clamped = Math.Max(0d, Math.Min(3d, score));
            var low = (int)Math.Floor(clamped);
            var high = (int)Math.Ceiling(clamped);
            var upperWeight = clamped - low;
            var probabilities = new double[4];
            probabilities[low] = 1d - upperWeight;
            probabilities[high] += upperWeight;
            var pointLevel = upperWeight <= 0.5d ? low : high;

            return new StressDecision(
                StressDecisionMode.Point,
                pointLevel,
                null,
                null,
                Labels[pointLevel],
                probabilities[pointLevel],
                null,
                new StressProbabilityVector(
                    probabilities[0],
                    probabilities[1],
                    probabilities[2],
                    probabilities[3]),
                clamped);
        }
    }
}
