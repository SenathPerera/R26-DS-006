using System;
using System.Collections.Generic;

namespace LaminarVR.AdaptiveMeditation.Physiology
{
    public enum PhysiologyTrendResultCode
    {
        Available,
        InsufficientSamples,
        InvalidWindow,
        NonIncreasingSequence,
        NonIncreasingTimestamp
    }

    public readonly struct PhysiologyTrendResult
    {
        internal PhysiologyTrendResult(
            PhysiologyTrendResultCode resultCode,
            int sampleCount,
            long firstSequenceNumber,
            long lastSequenceNumber,
            double stressScorePerMinute,
            double heartRateBpmPerMinute,
            double? rmssdMsPerMinute)
        {
            ResultCode = resultCode;
            SampleCount = sampleCount;
            FirstSequenceNumber = firstSequenceNumber;
            LastSequenceNumber = lastSequenceNumber;
            StressScorePerMinute = stressScorePerMinute;
            HeartRateBpmPerMinute = heartRateBpmPerMinute;
            RmssdMsPerMinute = rmssdMsPerMinute;
        }

        public PhysiologyTrendResultCode ResultCode { get; }

        public bool Available => ResultCode == PhysiologyTrendResultCode.Available;

        public int SampleCount { get; }

        public long FirstSequenceNumber { get; }

        public long LastSequenceNumber { get; }

        public double StressScorePerMinute { get; }

        public double HeartRateBpmPerMinute { get; }

        public double? RmssdMsPerMinute { get; }
    }

    public static class PhysiologyTrendCalculator
    {
        public static PhysiologyTrendResult Calculate(
            IReadOnlyList<PhysiologyWindowSnapshot> snapshots,
            int minimumSamples)
        {
            if (snapshots == null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }

            if (minimumSamples < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumSamples),
                    minimumSamples,
                    "At least two samples are required for a trend.");
            }

            if (snapshots.Count < minimumSamples)
            {
                return Invalid(
                    PhysiologyTrendResultCode.InsufficientSamples,
                    snapshots.Count);
            }

            var first = snapshots[0];
            if (!IsValid(first))
            {
                return Invalid(
                    PhysiologyTrendResultCode.InvalidWindow,
                    snapshots.Count);
            }

            var firstTimestamp = first.Window.WindowEndUtcUnixSeconds;
            var xMean = 0d;
            var stressMean = 0d;
            var heartRateMean = 0d;
            var rmssdMean = 0d;
            var hasCompleteRmssd = true;

            for (var index = 0; index < snapshots.Count; index++)
            {
                var snapshot = snapshots[index];
                if (!IsValid(snapshot))
                {
                    return Invalid(
                        PhysiologyTrendResultCode.InvalidWindow,
                        snapshots.Count);
                }

                if (index > 0)
                {
                    var previous = snapshots[index - 1];
                    if (snapshot.SequenceNumber <= previous.SequenceNumber)
                    {
                        return Invalid(
                            PhysiologyTrendResultCode.NonIncreasingSequence,
                            snapshots.Count);
                    }

                    if (snapshot.Window.WindowEndUtcUnixSeconds
                        <= previous.Window.WindowEndUtcUnixSeconds)
                    {
                        return Invalid(
                            PhysiologyTrendResultCode.NonIncreasingTimestamp,
                            snapshots.Count);
                    }
                }

                var minutes =
                    (snapshot.Window.WindowEndUtcUnixSeconds - firstTimestamp)
                    / 60d;
                xMean += minutes;
                stressMean += snapshot.Window.Stress.ContinuousScore;
                heartRateMean += snapshot.Window.HeartRateBpm;
                if (snapshot.Window.RmssdMs.HasValue)
                {
                    rmssdMean += snapshot.Window.RmssdMs.Value;
                }
                else
                {
                    hasCompleteRmssd = false;
                }
            }

            xMean /= snapshots.Count;
            stressMean /= snapshots.Count;
            heartRateMean /= snapshots.Count;
            if (hasCompleteRmssd)
            {
                rmssdMean /= snapshots.Count;
            }

            var xVariance = 0d;
            var stressCovariance = 0d;
            var heartRateCovariance = 0d;
            var rmssdCovariance = 0d;
            for (var index = 0; index < snapshots.Count; index++)
            {
                var snapshot = snapshots[index];
                var minutes =
                    (snapshot.Window.WindowEndUtcUnixSeconds - firstTimestamp)
                    / 60d;
                var xDifference = minutes - xMean;
                xVariance += xDifference * xDifference;
                stressCovariance += xDifference
                    * (snapshot.Window.Stress.ContinuousScore - stressMean);
                heartRateCovariance += xDifference
                    * (snapshot.Window.HeartRateBpm - heartRateMean);
                if (hasCompleteRmssd)
                {
                    rmssdCovariance += xDifference
                        * (snapshot.Window.RmssdMs.Value - rmssdMean);
                }
            }

            if (xVariance <= 0d)
            {
                return Invalid(
                    PhysiologyTrendResultCode.NonIncreasingTimestamp,
                    snapshots.Count);
            }

            return new PhysiologyTrendResult(
                PhysiologyTrendResultCode.Available,
                snapshots.Count,
                first.SequenceNumber,
                snapshots[snapshots.Count - 1].SequenceNumber,
                stressCovariance / xVariance,
                heartRateCovariance / xVariance,
                hasCompleteRmssd
                    ? rmssdCovariance / xVariance
                    : (double?)null);
        }

        private static bool IsValid(PhysiologyWindowSnapshot snapshot)
        {
            return snapshot.SequenceNumber > 0L
                && snapshot.Window != null
                && snapshot.Window.Stress != null
                && IsFinite(snapshot.Window.WindowEndUtcUnixSeconds)
                && IsFinite(snapshot.Window.Stress.ContinuousScore)
                && IsFinite(snapshot.Window.HeartRateBpm)
                && (!snapshot.Window.RmssdMs.HasValue
                    || IsFinite(snapshot.Window.RmssdMs.Value));
        }

        private static PhysiologyTrendResult Invalid(
            PhysiologyTrendResultCode resultCode,
            int sampleCount)
        {
            return new PhysiologyTrendResult(
                resultCode,
                sampleCount,
                0L,
                0L,
                0d,
                0d,
                null);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
