using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class EvaluationStatisticsTests
    {
        [Test]
        public void Describe_UsesSampleDeviationAndTypeSevenQuartiles()
        {
            var result = EvaluationStatistics.Describe(
                new[] { 1d, 2d, 3d, 4d, 5d });

            Assert.That(result.Count, Is.EqualTo(5));
            Assert.That(result.Mean, Is.EqualTo(3d));
            Assert.That(result.StandardDeviation, Is.EqualTo(
                System.Math.Sqrt(2.5d)).Within(1e-12d));
            Assert.That(result.Median, Is.EqualTo(3d));
            Assert.That(result.FirstQuartile, Is.EqualTo(2d));
            Assert.That(result.ThirdQuartile, Is.EqualTo(4d));
        }

        [Test]
        public void PairedMeanDifference_IsDeterministicAndPreservesPairs()
        {
            var left = new[] { 1d, 2d, 3d, 4d };
            var right = new[] { 2d, 4d, 4d, 8d };

            var first = EvaluationStatistics.PairedMeanDifference(
                left,
                right,
                1000,
                42);
            var replay = EvaluationStatistics.PairedMeanDifference(
                left,
                right,
                1000,
                42);

            Assert.That(first.MeanDifference, Is.EqualTo(-2d));
            Assert.That(first.ProbabilityLeftLower, Is.EqualTo(1d));
            Assert.That(replay.Lower95, Is.EqualTo(first.Lower95));
            Assert.That(replay.Upper95, Is.EqualTo(first.Upper95));
        }

        [Test]
        public void PairedSignFlipMeanDifference_IsDeterministicWithAddOnePValue()
        {
            var left = new[] { 0d, 0d, 0d, 0d };
            var right = new[] { 1d, 1d, 1d, 1d };

            var first = EvaluationStatistics.PairedSignFlipMeanDifference(
                left,
                right,
                1000,
                42);
            var replay = EvaluationStatistics.PairedSignFlipMeanDifference(
                left,
                right,
                1000,
                42);

            Assert.That(first.ObservedMeanDifference, Is.EqualTo(-1d));
            Assert.That(first.TwoSidedPValue,
                Is.EqualTo((first.ExtremeResampleCount + 1d) / 1001d));
            Assert.That(replay.ExtremeResampleCount,
                Is.EqualTo(first.ExtremeResampleCount));
            Assert.That(replay.TwoSidedPValue,
                Is.EqualTo(first.TwoSidedPValue));
        }

        [Test]
        public void PairedSignFlipMeanDifference_AllZeroDifferences_ReturnsOne()
        {
            var values = new[] { 1d, 2d, 3d };

            var result = EvaluationStatistics.PairedSignFlipMeanDifference(
                values,
                values,
                100,
                42);

            Assert.That(result.TwoSidedPValue, Is.EqualTo(1d));
            Assert.That(result.ExtremeResampleCount, Is.EqualTo(100));
        }

        [Test]
        public void HolmAdjust_PreservesOrderAndEnforcesMonotonicAdjustment()
        {
            var adjusted = EvaluationStatistics.HolmAdjust(
                new[] { 0.04d, 0.01d, 0.03d });

            Assert.That(adjusted[0], Is.EqualTo(0.06d).Within(1e-12d));
            Assert.That(adjusted[1], Is.EqualTo(0.03d).Within(1e-12d));
            Assert.That(adjusted[2], Is.EqualTo(0.06d).Within(1e-12d));
        }
    }
}
