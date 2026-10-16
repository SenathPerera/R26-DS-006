using System;
using System.Collections.Generic;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public readonly struct EvaluationDescriptiveStatistics
    {
        public EvaluationDescriptiveStatistics(
            int count,
            double mean,
            double standardDeviation,
            double median,
            double firstQuartile,
            double thirdQuartile)
        {
            Count = count;
            Mean = mean;
            StandardDeviation = standardDeviation;
            Median = median;
            FirstQuartile = firstQuartile;
            ThirdQuartile = thirdQuartile;
        }

        public int Count { get; }
        public double Mean { get; }
        public double StandardDeviation { get; }
        public double Median { get; }
        public double FirstQuartile { get; }
        public double ThirdQuartile { get; }
        public double InterquartileRange => ThirdQuartile - FirstQuartile;
    }

    public readonly struct PairedBootstrapResult
    {
        public PairedBootstrapResult(
            int pairCount,
            int resampleCount,
            double meanDifference,
            double medianDifference,
            double lower95,
            double upper95,
            double probabilityLeftLower)
        {
            PairCount = pairCount;
            ResampleCount = resampleCount;
            MeanDifference = meanDifference;
            MedianDifference = medianDifference;
            Lower95 = lower95;
            Upper95 = upper95;
            ProbabilityLeftLower = probabilityLeftLower;
        }

        public int PairCount { get; }
        public int ResampleCount { get; }
        public double MeanDifference { get; }
        public double MedianDifference { get; }
        public double Lower95 { get; }
        public double Upper95 { get; }
        public double ProbabilityLeftLower { get; }
    }

    public readonly struct PairedPermutationResult
    {
        public PairedPermutationResult(
            int pairCount,
            int resampleCount,
            double observedMeanDifference,
            double twoSidedPValue,
            int extremeResampleCount)
        {
            PairCount = pairCount;
            ResampleCount = resampleCount;
            ObservedMeanDifference = observedMeanDifference;
            TwoSidedPValue = twoSidedPValue;
            ExtremeResampleCount = extremeResampleCount;
        }

        public int PairCount { get; }
        public int ResampleCount { get; }
        public double ObservedMeanDifference { get; }
        public double TwoSidedPValue { get; }
        public int ExtremeResampleCount { get; }
    }

    public static class EvaluationStatistics
    {
        public const int ProtocolBootstrapResamples = 10000;
        public const int ProtocolBootstrapSeed = 910001;
        public const int ProtocolPermutationResamples = 100000;
        public const int ProtocolPermutationSeed = 920001;
        private const ulong BootstrapStream = 0x4253545250414952UL;
        private const ulong PermutationStream = 0x50524D5041495232UL;

        public static EvaluationDescriptiveStatistics Describe(
            IReadOnlyList<double> values)
        {
            ValidateValues(values, minimumCount: 1);
            var sorted = CopyAndSort(values);
            var mean = Mean(sorted);
            var squaredDifferenceSum = 0d;
            for (var index = 0; index < sorted.Length; index++)
            {
                var difference = sorted[index] - mean;
                squaredDifferenceSum += difference * difference;
            }

            return new EvaluationDescriptiveStatistics(
                sorted.Length,
                mean,
                sorted.Length > 1
                    ? Math.Sqrt(squaredDifferenceSum / (sorted.Length - 1))
                    : 0d,
                QuantileType7(sorted, 0.5d),
                QuantileType7(sorted, 0.25d),
                QuantileType7(sorted, 0.75d));
        }

        public static PairedBootstrapResult PairedMeanDifference(
            IReadOnlyList<double> left,
            IReadOnlyList<double> right,
            int resampleCount = ProtocolBootstrapResamples,
            int seed = ProtocolBootstrapSeed)
        {
            ValidateValues(left, minimumCount: 2);
            ValidateValues(right, minimumCount: 2);
            if (left.Count != right.Count)
            {
                throw new ArgumentException(
                    "Paired samples must have equal length.");
            }

            if (resampleCount < 1 || seed < 0)
            {
                throw new ArgumentOutOfRangeException(
                    resampleCount < 1 ? nameof(resampleCount) : nameof(seed));
            }

            var differences = new double[left.Count];
            var leftLowerScore = 0d;
            for (var index = 0; index < differences.Length; index++)
            {
                differences[index] = left[index] - right[index];
                if (differences[index] < 0d)
                {
                    leftLowerScore += 1d;
                }
                else if (differences[index] == 0d)
                {
                    leftLowerScore += 0.5d;
                }
            }

            var random = new Pcg32((ulong)seed, BootstrapStream);
            var bootstrapMeans = new double[resampleCount];
            for (var resample = 0;
                resample < bootstrapMeans.Length;
                resample++)
            {
                var sum = 0d;
                for (var draw = 0; draw < differences.Length; draw++)
                {
                    sum += differences[random.NextInt(differences.Length)];
                }

                bootstrapMeans[resample] = sum / differences.Length;
            }

            Array.Sort(bootstrapMeans);
            return new PairedBootstrapResult(
                differences.Length,
                resampleCount,
                Mean(differences),
                Describe(differences).Median,
                QuantileType7(bootstrapMeans, 0.025d),
                QuantileType7(bootstrapMeans, 0.975d),
                leftLowerScore / differences.Length);
        }

        public static PairedPermutationResult PairedSignFlipMeanDifference(
            IReadOnlyList<double> left,
            IReadOnlyList<double> right,
            int resampleCount = ProtocolPermutationResamples,
            int seed = ProtocolPermutationSeed)
        {
            ValidateValues(left, minimumCount: 2);
            ValidateValues(right, minimumCount: 2);
            if (left.Count != right.Count)
            {
                throw new ArgumentException(
                    "Paired samples must have equal length.");
            }

            if (resampleCount < 1 || seed < 0)
            {
                throw new ArgumentOutOfRangeException(
                    resampleCount < 1 ? nameof(resampleCount) : nameof(seed));
            }

            var differences = new double[left.Count];
            for (var index = 0; index < differences.Length; index++)
            {
                differences[index] = left[index] - right[index];
            }

            var observedMean = Mean(differences);
            var observedAbsoluteSum = Math.Abs(
                observedMean * differences.Length);
            var random = new Pcg32((ulong)seed, PermutationStream);
            var extremeCount = 0;
            for (var resample = 0; resample < resampleCount; resample++)
            {
                var signedSum = 0d;
                for (var pair = 0; pair < differences.Length; pair++)
                {
                    signedSum += (random.NextUInt() & 1u) == 0u
                        ? differences[pair]
                        : -differences[pair];
                }

                if (Math.Abs(signedSum) + 1e-12d
                    >= observedAbsoluteSum)
                {
                    extremeCount++;
                }
            }

            return new PairedPermutationResult(
                differences.Length,
                resampleCount,
                observedMean,
                (extremeCount + 1d) / (resampleCount + 1d),
                extremeCount);
        }

        public static double[] HolmAdjust(IReadOnlyList<double> pValues)
        {
            if (pValues == null)
            {
                throw new ArgumentNullException(nameof(pValues));
            }

            var ordered = new KeyValuePair<int, double>[pValues.Count];
            for (var index = 0; index < pValues.Count; index++)
            {
                var value = pValues[index];
                if (double.IsNaN(value)
                    || double.IsInfinity(value)
                    || value < 0d
                    || value > 1d)
                {
                    throw new ArgumentOutOfRangeException(nameof(pValues));
                }

                ordered[index] = new KeyValuePair<int, double>(index, value);
            }

            Array.Sort(
                ordered,
                (left, right) => left.Value.CompareTo(right.Value));
            var adjusted = new double[pValues.Count];
            var runningMaximum = 0d;
            for (var rank = 0; rank < ordered.Length; rank++)
            {
                var candidate = Math.Min(
                    1d,
                    (ordered.Length - rank) * ordered[rank].Value);
                runningMaximum = Math.Max(runningMaximum, candidate);
                adjusted[ordered[rank].Key] = runningMaximum;
            }

            return adjusted;
        }

        private static double[] CopyAndSort(IReadOnlyList<double> values)
        {
            var copy = new double[values.Count];
            for (var index = 0; index < values.Count; index++)
            {
                copy[index] = values[index];
            }

            Array.Sort(copy);
            return copy;
        }

        private static double Mean(IReadOnlyList<double> values)
        {
            var sum = 0d;
            for (var index = 0; index < values.Count; index++)
            {
                sum += values[index];
            }

            return sum / values.Count;
        }

        private static double QuantileType7(double[] sorted, double probability)
        {
            if (probability < 0d || probability > 1d)
            {
                throw new ArgumentOutOfRangeException(nameof(probability));
            }

            if (sorted.Length == 1)
            {
                return sorted[0];
            }

            var position = (sorted.Length - 1) * probability;
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            var fraction = position - lower;
            return sorted[lower]
                + (fraction * (sorted[upper] - sorted[lower]));
        }

        private static void ValidateValues(
            IReadOnlyList<double> values,
            int minimumCount)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Count < minimumCount)
            {
                throw new ArgumentException(
                    "The sample does not contain enough values.",
                    nameof(values));
            }

            for (var index = 0; index < values.Count; index++)
            {
                if (double.IsNaN(values[index])
                    || double.IsInfinity(values[index]))
                {
                    throw new ArgumentException(
                        "Samples must contain only finite values.",
                        nameof(values));
                }
            }
        }
    }
}
