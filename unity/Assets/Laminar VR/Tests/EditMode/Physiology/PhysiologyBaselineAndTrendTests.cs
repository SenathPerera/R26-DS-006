using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Physiology
{
    public sealed class PhysiologyBaselineAndTrendTests
    {
        [Test]
        public void BaselineAccumulator_ComputesExplicitPopulationAndSampleStatistics()
        {
            var accumulator = new PhysiologyBaselineAccumulator();
            accumulator.TryAdd(CreateSnapshot(1L, 1000d, 1d, 70d, 20d));
            accumulator.TryAdd(CreateSnapshot(2L, 1060d, 3d, 74d, 24d));

            var population = accumulator.CreateBaseline(
                BaselineStandardDeviationMethod.Population);
            var sample = accumulator.CreateBaseline(
                BaselineStandardDeviationMethod.Sample);

            Assert.That(population.Stress.SampleCount, Is.EqualTo(2));
            Assert.That(population.Stress.Mean, Is.EqualTo(2d));
            Assert.That(population.Stress.StandardDeviation, Is.EqualTo(1d));
            Assert.That(population.HeartRate.Mean, Is.EqualTo(72d));
            Assert.That(population.Rmssd.Mean, Is.EqualTo(22d));
            Assert.That(
                sample.Stress.StandardDeviation,
                Is.EqualTo(Math.Sqrt(2d)).Within(1e-12d));
        }

        [Test]
        public void BaselineAccumulator_RejectsNonIncreasingSequence()
        {
            var accumulator = new PhysiologyBaselineAccumulator();

            var first = accumulator.TryAdd(
                CreateSnapshot(2L, 1000d, 1d, 70d, 20d));
            var duplicate = accumulator.TryAdd(
                CreateSnapshot(2L, 1060d, 2d, 72d, 22d));

            Assert.That(first, Is.EqualTo(PhysiologyBaselineAddResult.Accepted));
            Assert.That(
                duplicate,
                Is.EqualTo(PhysiologyBaselineAddResult.NonIncreasingSequence));
            Assert.That(accumulator.AcceptedWindowCount, Is.EqualTo(1));
        }

        [Test]
        public void BaselineAccumulator_TracksMissingRmssdWithoutFabricatingIt()
        {
            var accumulator = new PhysiologyBaselineAccumulator();
            accumulator.TryAdd(CreateSnapshot(1L, 1000d, 1d, 70d, null));
            accumulator.TryAdd(CreateSnapshot(2L, 1060d, 2d, 72d, 22d));

            var baseline = accumulator.CreateBaseline(
                BaselineStandardDeviationMethod.Population);

            Assert.That(baseline.Stress.SampleCount, Is.EqualTo(2));
            Assert.That(baseline.Rmssd.SampleCount, Is.EqualTo(1));
            Assert.That(baseline.Rmssd.Mean, Is.EqualTo(22d));
        }

        [Test]
        public void TrendCalculator_ComputesLeastSquaresSlopesPerMinute()
        {
            var snapshots = new[]
            {
                CreateSnapshot(1L, 1000d, 3d, 70d, 20d),
                CreateSnapshot(2L, 1060d, 2d, 72d, 23d),
                CreateSnapshot(3L, 1120d, 1d, 74d, 26d)
            };

            var trend = PhysiologyTrendCalculator.Calculate(snapshots, 3);

            Assert.That(trend.Available, Is.True);
            Assert.That(trend.StressScorePerMinute, Is.EqualTo(-1d));
            Assert.That(trend.HeartRateBpmPerMinute, Is.EqualTo(2d));
            Assert.That(trend.RmssdMsPerMinute, Is.EqualTo(3d));
            Assert.That(trend.FirstSequenceNumber, Is.EqualTo(1L));
            Assert.That(trend.LastSequenceNumber, Is.EqualTo(3L));
        }

        [Test]
        public void TrendCalculator_PreservesMissingRmssdAsUnavailable()
        {
            var snapshots = new[]
            {
                CreateSnapshot(1L, 1000d, 3d, 70d, null),
                CreateSnapshot(2L, 1060d, 2d, 72d, 23d)
            };

            var trend = PhysiologyTrendCalculator.Calculate(snapshots, 2);

            Assert.That(trend.Available, Is.True);
            Assert.That(trend.RmssdMsPerMinute, Is.Null);
        }

        [Test]
        public void TrendCalculator_RejectsInsufficientOrNonIncreasingData()
        {
            var one = new[]
            {
                CreateSnapshot(1L, 1000d, 3d, 70d, 20d)
            };
            var duplicateSequence = new[]
            {
                CreateSnapshot(1L, 1000d, 3d, 70d, 20d),
                CreateSnapshot(1L, 1060d, 2d, 72d, 23d)
            };

            var insufficient = PhysiologyTrendCalculator.Calculate(one, 2);
            var invalid = PhysiologyTrendCalculator.Calculate(
                duplicateSequence,
                2);

            Assert.That(
                insufficient.ResultCode,
                Is.EqualTo(PhysiologyTrendResultCode.InsufficientSamples));
            Assert.That(
                invalid.ResultCode,
                Is.EqualTo(PhysiologyTrendResultCode.NonIncreasingSequence));
        }

        private static PhysiologyWindowSnapshot CreateSnapshot(
            long sequenceNumber,
            double windowEndUtcUnixSeconds,
            double stressScore,
            double heartRateBpm,
            double? rmssdMs)
        {
            return new PhysiologyWindowSnapshot(
                sequenceNumber,
                new PhysiologyWindow(
                    windowEndUtcUnixSeconds,
                    windowEndUtcUnixSeconds - 60d,
                    windowEndUtcUnixSeconds,
                    heartRateBpm,
                    rmssdMs,
                    40d,
                    new StressDecision(
                        StressDecisionMode.Point,
                        2,
                        null,
                        null,
                        "moderate",
                        0.5d,
                        false,
                        new StressProbabilityVector(
                            0.1d,
                            0.2d,
                            0.6d,
                            0.1d),
                        stressScore),
                    0.95d),
                0d,
                sequenceNumber);
        }
    }
}
