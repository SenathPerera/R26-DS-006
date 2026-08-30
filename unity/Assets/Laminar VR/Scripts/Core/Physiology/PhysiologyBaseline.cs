using System;

namespace LaminarVR.AdaptiveMeditation.Physiology
{
    public enum BaselineStandardDeviationMethod
    {
        Population,
        Sample
    }

    public enum PhysiologyBaselineAddResult
    {
        Accepted,
        InvalidSnapshot,
        NonIncreasingSequence
    }

    public readonly struct PhysiologyMetricStatistics
    {
        public PhysiologyMetricStatistics(
            int sampleCount,
            double mean,
            double standardDeviation)
        {
            SampleCount = sampleCount;
            Mean = mean;
            StandardDeviation = standardDeviation;
        }

        public int SampleCount { get; }

        public double Mean { get; }

        public double StandardDeviation { get; }
    }

    public sealed class PhysiologyBaseline
    {
        public PhysiologyBaseline(
            BaselineStandardDeviationMethod standardDeviationMethod,
            PhysiologyMetricStatistics stress,
            PhysiologyMetricStatistics heartRate,
            PhysiologyMetricStatistics rmssd)
        {
            StandardDeviationMethod = standardDeviationMethod;
            Stress = stress;
            HeartRate = heartRate;
            Rmssd = rmssd;
        }

        public BaselineStandardDeviationMethod StandardDeviationMethod { get; }

        public PhysiologyMetricStatistics Stress { get; }

        public PhysiologyMetricStatistics HeartRate { get; }

        public PhysiologyMetricStatistics Rmssd { get; }
    }

    public sealed class PhysiologyBaselineAccumulator
    {
        private RunningStatistics stress;
        private RunningStatistics heartRate;
        private RunningStatistics rmssd;
        private long latestSequenceNumber;

        public int AcceptedWindowCount => stress.Count;

        public long LatestSequenceNumber => latestSequenceNumber;

        public PhysiologyBaselineAddResult TryAdd(
            PhysiologyWindowSnapshot snapshot)
        {
            if (!IsValidSnapshot(snapshot))
            {
                return PhysiologyBaselineAddResult.InvalidSnapshot;
            }

            if (snapshot.SequenceNumber <= latestSequenceNumber)
            {
                return PhysiologyBaselineAddResult.NonIncreasingSequence;
            }

            stress.Add(snapshot.Window.Stress.ContinuousScore);
            heartRate.Add(snapshot.Window.HeartRateBpm);
            if (snapshot.Window.RmssdMs.HasValue)
            {
                rmssd.Add(snapshot.Window.RmssdMs.Value);
            }

            latestSequenceNumber = snapshot.SequenceNumber;
            return PhysiologyBaselineAddResult.Accepted;
        }

        public PhysiologyBaseline CreateBaseline(
            BaselineStandardDeviationMethod method)
        {
            if (!Enum.IsDefined(typeof(BaselineStandardDeviationMethod), method))
            {
                throw new ArgumentOutOfRangeException(nameof(method));
            }

            if (stress.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one valid physiology window is required.");
            }

            return new PhysiologyBaseline(
                method,
                stress.CreateMetric(method),
                heartRate.CreateMetric(method),
                rmssd.CreateMetric(method));
        }

        private static bool IsValidSnapshot(PhysiologyWindowSnapshot snapshot)
        {
            if (snapshot.SequenceNumber < 1L || snapshot.Window == null)
            {
                return false;
            }

            var window = snapshot.Window;
            return window.Stress != null
                && IsFinite(window.Stress.ContinuousScore)
                && IsFinite(window.HeartRateBpm)
                && (!window.RmssdMs.HasValue
                    || IsFinite(window.RmssdMs.Value));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private struct RunningStatistics
        {
            private double mean;
            private double sumSquaredDifferences;

            public int Count { get; private set; }

            public void Add(double value)
            {
                Count++;
                var difference = value - mean;
                mean += difference / Count;
                var updatedDifference = value - mean;
                sumSquaredDifferences += difference * updatedDifference;
            }

            public PhysiologyMetricStatistics CreateMetric(
                BaselineStandardDeviationMethod method)
            {
                if (Count == 0)
                {
                    return new PhysiologyMetricStatistics(0, 0d, 0d);
                }

                var denominator = method
                    == BaselineStandardDeviationMethod.Sample
                        ? Count - 1
                        : Count;
                var variance = denominator > 0
                    ? sumSquaredDifferences / denominator
                    : 0d;
                return new PhysiologyMetricStatistics(
                    Count,
                    mean,
                    Math.Sqrt(Math.Max(0d, variance)));
            }
        }
    }
}
