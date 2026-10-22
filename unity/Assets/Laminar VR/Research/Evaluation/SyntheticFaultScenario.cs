using System;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public enum SyntheticFaultScenarioId
    {
        R0Nominal = 0,
        R1ReducedNoise = 1,
        R2HighNoise = 2,
        R3MildLoss = 3,
        R4SevereLoss = 4,
        R5IntermittentPoorQuality = 5,
        R6ModerateArrivalLatency = 6,
        R7HighArrivalLatency = 7,
        R8StaleBurst = 8,
        R9OutOfOrderPair = 9,
        R10NetworkGap = 10,
        R11CombinedModerate = 11,
        R12CombinedSevere = 12
    }

    public sealed class SyntheticFaultScenarioDefinition
    {
        internal SyntheticFaultScenarioDefinition(
            SyntheticFaultScenarioId id,
            string description,
            double noiseMultiplier,
            double dropProbability,
            double arrivalLatencySeconds,
            int lowQualityEveryNthEmission,
            bool staleBurst,
            bool reverseOutOfOrderPair,
            double? networkGapStartSeconds,
            double? networkGapEndSeconds)
        {
            Id = id;
            Description = description;
            NoiseMultiplier = noiseMultiplier;
            DropProbability = dropProbability;
            ArrivalLatencySeconds = arrivalLatencySeconds;
            LowQualityEveryNthEmission = lowQualityEveryNthEmission;
            StaleBurst = staleBurst;
            ReverseOutOfOrderPair = reverseOutOfOrderPair;
            NetworkGapStartSeconds = networkGapStartSeconds;
            NetworkGapEndSeconds = networkGapEndSeconds;
        }

        public SyntheticFaultScenarioId Id { get; }
        public string Code => "R" + (int)Id;
        public string Description { get; }
        public double NoiseMultiplier { get; }
        public double DropProbability { get; }
        public double ArrivalLatencySeconds { get; }
        public int LowQualityEveryNthEmission { get; }
        public bool StaleBurst { get; }
        public bool ReverseOutOfOrderPair { get; }
        public double? NetworkGapStartSeconds { get; }
        public double? NetworkGapEndSeconds { get; }

        public bool HasNetworkGap => NetworkGapStartSeconds.HasValue;

        public IEvaluationNetworkSchedule CreateNetworkSchedule()
        {
            return HasNetworkGap
                ? new IntervalEvaluationNetworkSchedule(
                    NetworkGapStartSeconds.Value,
                    NetworkGapEndSeconds.Value)
                : (IEvaluationNetworkSchedule)
                    AlwaysConnectedEvaluationNetworkSchedule.Instance;
        }
    }

    public static class SyntheticFaultScenarioCatalog
    {
        private static readonly SyntheticFaultScenarioDefinition[] Definitions =
        {
            Create(SyntheticFaultScenarioId.R0Nominal, "Nominal"),
            Create(
                SyntheticFaultScenarioId.R1ReducedNoise,
                "Reduced noise",
                noiseMultiplier: 0.5d),
            Create(
                SyntheticFaultScenarioId.R2HighNoise,
                "High noise",
                noiseMultiplier: 2d),
            Create(
                SyntheticFaultScenarioId.R3MildLoss,
                "Mild loss",
                dropProbability: 0.1d),
            Create(
                SyntheticFaultScenarioId.R4SevereLoss,
                "Severe loss",
                dropProbability: 0.3d),
            Create(
                SyntheticFaultScenarioId.R5IntermittentPoorQuality,
                "Intermittent poor quality",
                lowQualityEveryNthEmission: 4),
            Create(
                SyntheticFaultScenarioId.R6ModerateArrivalLatency,
                "Moderate arrival latency",
                arrivalLatencySeconds: 30d),
            Create(
                SyntheticFaultScenarioId.R7HighArrivalLatency,
                "High arrival latency",
                arrivalLatencySeconds: 75d),
            Create(
                SyntheticFaultScenarioId.R8StaleBurst,
                "Stale burst",
                staleBurst: true),
            Create(
                SyntheticFaultScenarioId.R9OutOfOrderPair,
                "Out-of-order pair",
                reverseOutOfOrderPair: true),
            Create(
                SyntheticFaultScenarioId.R10NetworkGap,
                "Network gap",
                networkGapStartSeconds: 420d,
                networkGapEndSeconds: 600d),
            Create(
                SyntheticFaultScenarioId.R11CombinedModerate,
                "Combined moderate",
                noiseMultiplier: 1.5d,
                dropProbability: 0.15d,
                arrivalLatencySeconds: 15d,
                lowQualityEveryNthEmission: 6),
            Create(
                SyntheticFaultScenarioId.R12CombinedSevere,
                "Combined severe",
                noiseMultiplier: 2.5d,
                dropProbability: 0.3d,
                arrivalLatencySeconds: 60d,
                lowQualityEveryNthEmission: 4,
                networkGapStartSeconds: 600d,
                networkGapEndSeconds: 780d)
        };

        public static int Count => Definitions.Length;

        public static SyntheticFaultScenarioDefinition Get(
            SyntheticFaultScenarioId id)
        {
            if (!Enum.IsDefined(typeof(SyntheticFaultScenarioId), id))
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            return Definitions[(int)id];
        }

        public static SyntheticFaultScenarioDefinition GetAt(int index)
        {
            return Definitions[index];
        }

        private static SyntheticFaultScenarioDefinition Create(
            SyntheticFaultScenarioId id,
            string description,
            double noiseMultiplier = 1d,
            double dropProbability = 0d,
            double arrivalLatencySeconds = 0d,
            int lowQualityEveryNthEmission = 0,
            bool staleBurst = false,
            bool reverseOutOfOrderPair = false,
            double? networkGapStartSeconds = null,
            double? networkGapEndSeconds = null)
        {
            return new SyntheticFaultScenarioDefinition(
                id,
                description,
                noiseMultiplier,
                dropProbability,
                arrivalLatencySeconds,
                lowQualityEveryNthEmission,
                staleBurst,
                reverseOutOfOrderPair,
                networkGapStartSeconds,
                networkGapEndSeconds);
        }
    }
}
