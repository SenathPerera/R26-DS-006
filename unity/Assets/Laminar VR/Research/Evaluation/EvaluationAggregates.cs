using System;
using System.Collections.Generic;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public readonly struct EvaluationAggregateKey
        : IEquatable<EvaluationAggregateKey>
    {
        public EvaluationAggregateKey(
            string experimentId,
            SyntheticArchetypeId archetype,
            string scenarioCode,
            EvaluationPolicyCondition policy)
        {
            ExperimentId = experimentId ?? throw new ArgumentNullException(
                nameof(experimentId));
            Archetype = archetype;
            ScenarioCode = scenarioCode ?? throw new ArgumentNullException(
                nameof(scenarioCode));
            Policy = policy;
        }

        public string ExperimentId { get; }
        public SyntheticArchetypeId Archetype { get; }
        public string ScenarioCode { get; }
        public EvaluationPolicyCondition Policy { get; }

        public bool Equals(EvaluationAggregateKey other)
        {
            return string.Equals(
                    ExperimentId,
                    other.ExperimentId,
                    StringComparison.Ordinal)
                && Archetype == other.Archetype
                && string.Equals(
                    ScenarioCode,
                    other.ScenarioCode,
                    StringComparison.Ordinal)
                && Policy == other.Policy;
        }

        public override bool Equals(object obj)
        {
            return obj is EvaluationAggregateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + ExperimentId.GetHashCode();
                hash = (hash * 31) + Archetype.GetHashCode();
                hash = (hash * 31) + ScenarioCode.GetHashCode();
                hash = (hash * 31) + Policy.GetHashCode();
                return hash;
            }
        }
    }

    public sealed class EvaluationAggregateRow
    {
        public EvaluationAggregateRow(
            EvaluationAggregateKey key,
            int attemptedSessions,
            int metricAvailableSessions,
            EvaluationDescriptiveStatistics? meanRegretStatistics,
            int sceneLimitViolations,
            int multiDimensionViolations,
            int invalidModelUpdates)
        {
            Key = key;
            AttemptedSessions = attemptedSessions;
            MetricAvailableSessions = metricAvailableSessions;
            MeanRegretStatistics = meanRegretStatistics;
            SceneLimitViolations = sceneLimitViolations;
            MultiDimensionViolations = multiDimensionViolations;
            InvalidModelUpdates = invalidModelUpdates;
        }

        public EvaluationAggregateKey Key { get; }
        public int AttemptedSessions { get; }
        public int MetricAvailableSessions { get; }
        public EvaluationDescriptiveStatistics? MeanRegretStatistics { get; }
        public int SceneLimitViolations { get; }
        public int MultiDimensionViolations { get; }
        public int InvalidModelUpdates { get; }
    }

    public sealed class EvaluationPairedSamples
    {
        public EvaluationPairedSamples(double[] left, double[] right)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
            if (left.Length != right.Length)
            {
                throw new ArgumentException(
                    "Paired samples must have equal length.");
            }
        }

        public double[] Left { get; }
        public double[] Right { get; }
        public int Count => Left.Length;
    }

    public sealed class EvaluationConfirmatoryResult
    {
        public EvaluationConfirmatoryResult(
            string hypothesis,
            PairedBootstrapResult bootstrap,
            PairedPermutationResult permutation)
        {
            Hypothesis = hypothesis ?? throw new ArgumentNullException(
                nameof(hypothesis));
            Bootstrap = bootstrap;
            Permutation = permutation;
        }

        public string Hypothesis { get; }
        public PairedBootstrapResult Bootstrap { get; }
        public PairedPermutationResult Permutation { get; }
    }

    public static class EvaluationAggregateBuilder
    {
        public static EvaluationAggregateRow[] Build(
            IReadOnlyList<EvaluationSessionSummary> sessions)
        {
            if (sessions == null)
            {
                throw new ArgumentNullException(nameof(sessions));
            }

            var groups = new Dictionary<
                EvaluationAggregateKey,
                List<EvaluationSessionSummary>>();
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index] ?? throw new ArgumentException(
                    "Session summaries cannot contain null entries.",
                    nameof(sessions));
                var key = new EvaluationAggregateKey(
                    session.ExperimentId,
                    session.Archetype,
                    session.ScenarioCode,
                    session.PolicyCondition);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<EvaluationSessionSummary>();
                    groups.Add(key, group);
                }

                group.Add(session);
            }

            var rows = new List<EvaluationAggregateRow>(groups.Count);
            foreach (var pair in groups)
            {
                var regrets = new List<double>();
                var sceneViolations = 0;
                var multiDimensionViolations = 0;
                var invalidUpdates = 0;
                for (var index = 0; index < pair.Value.Count; index++)
                {
                    var session = pair.Value[index];
                    if (session.MeanPseudoRegret.HasValue)
                    {
                        regrets.Add(session.MeanPseudoRegret.Value);
                    }

                    sceneViolations += session.SceneLimitViolationCount;
                    multiDimensionViolations +=
                        session.MultiDimensionViolationCount;
                    invalidUpdates += session.InvalidModelUpdateCount;
                }

                rows.Add(new EvaluationAggregateRow(
                    pair.Key,
                    pair.Value.Count,
                    regrets.Count,
                    regrets.Count > 0
                        ? EvaluationStatistics.Describe(regrets)
                        : (EvaluationDescriptiveStatistics?)null,
                    sceneViolations,
                    multiDimensionViolations,
                    invalidUpdates));
            }

            rows.Sort(CompareRows);
            return rows.ToArray();
        }

        public static PairedBootstrapResult BuildPairedRegretContrast(
            IReadOnlyList<EvaluationSessionSummary> sessions,
            string experimentId,
            SyntheticArchetypeId archetype,
            string scenarioCode,
            EvaluationPolicyCondition left,
            EvaluationPolicyCondition right,
            int bootstrapSeed = EvaluationStatistics.ProtocolBootstrapSeed)
        {
            if (sessions == null)
            {
                throw new ArgumentNullException(nameof(sessions));
            }

            var leftBySeed = CollectBySeed(
                sessions,
                experimentId,
                archetype,
                scenarioCode,
                left);
            var rightBySeed = CollectBySeed(
                sessions,
                experimentId,
                archetype,
                scenarioCode,
                right);
            var seeds = new List<int>();
            foreach (var pair in leftBySeed)
            {
                if (rightBySeed.ContainsKey(pair.Key))
                {
                    seeds.Add(pair.Key);
                }
            }

            seeds.Sort();
            var leftValues = new double[seeds.Count];
            var rightValues = new double[seeds.Count];
            for (var index = 0; index < seeds.Count; index++)
            {
                leftValues[index] = leftBySeed[seeds[index]];
                rightValues[index] = rightBySeed[seeds[index]];
            }

            return EvaluationStatistics.PairedMeanDifference(
                leftValues,
                rightValues,
                EvaluationStatistics.ProtocolBootstrapResamples,
                bootstrapSeed);
        }

        public static PairedBootstrapResult BuildH3PrimaryContrast(
            IReadOnlyList<EvaluationSessionSummary> studyASessions,
            int bootstrapSeed = EvaluationStatistics.ProtocolBootstrapSeed)
        {
            var samples = BuildH3PrimarySamples(studyASessions);
            return EvaluationStatistics.PairedMeanDifference(
                samples.Left,
                samples.Right,
                EvaluationStatistics.ProtocolBootstrapResamples,
                bootstrapSeed);
        }

        public static EvaluationPairedSamples BuildH3PrimarySamples(
            IReadOnlyList<EvaluationSessionSummary> studyASessions)
        {
            if (studyASessions == null)
            {
                throw new ArgumentNullException(nameof(studyASessions));
            }

            var linUcb = new List<double>();
            var randomSafe = new List<double>();
            for (var seed = EvaluationExperimentRunner.StudyAFirstSeed;
                seed < EvaluationExperimentRunner.StudyAFirstSeed
                    + EvaluationExperimentRunner.StudyAReplicationCount;
                seed++)
            {
                var linUcbSum = 0d;
                var randomSafeSum = 0d;
                var complete = true;
                for (var archetypeValue = 1; archetypeValue <= 7;
                    archetypeValue++)
                {
                    var archetype = (SyntheticArchetypeId)archetypeValue;
                    if (!TryFindMeanRegret(
                            studyASessions,
                            "study-a",
                            archetype,
                            "R0",
                            EvaluationPolicyCondition.ContextualBandit,
                            seed,
                            out var linUcbValue)
                        || !TryFindMeanRegret(
                            studyASessions,
                            "study-a",
                            archetype,
                            "R0",
                            EvaluationPolicyCondition.RandomSafe,
                            seed,
                            out var randomSafeValue))
                    {
                        complete = false;
                        break;
                    }

                    linUcbSum += linUcbValue;
                    randomSafeSum += randomSafeValue;
                }

                if (complete)
                {
                    linUcb.Add(linUcbSum / 7d);
                    randomSafe.Add(randomSafeSum / 7d);
                }
            }

            return new EvaluationPairedSamples(
                linUcb.ToArray(),
                randomSafe.ToArray());
        }

        public static PairedBootstrapResult BuildH4NonStationarityContrast(
            IReadOnlyList<EvaluationSessionSummary> studyASessions,
            int bootstrapSeed = EvaluationStatistics.ProtocolBootstrapSeed)
        {
            var samples = BuildH4NonStationaritySamples(studyASessions);
            return EvaluationStatistics.PairedMeanDifference(
                samples.Left,
                samples.Right,
                EvaluationStatistics.ProtocolBootstrapResamples,
                bootstrapSeed);
        }

        public static EvaluationPairedSamples BuildH4NonStationaritySamples(
            IReadOnlyList<EvaluationSessionSummary> studyASessions)
        {
            return BuildMatchedArchetypeSamples(
                studyASessions,
                SyntheticArchetypeId.A8NonStationaryResponder,
                SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                EvaluationPolicyCondition.ContextualBandit);
        }

        public static EvaluationConfirmatoryResult BuildH3Inference(
            IReadOnlyList<EvaluationSessionSummary> studyASessions)
        {
            return BuildInference(
                "H3",
                BuildH3PrimarySamples(studyASessions));
        }

        public static EvaluationConfirmatoryResult BuildH4Inference(
            IReadOnlyList<EvaluationSessionSummary> studyASessions)
        {
            return BuildInference(
                "H4",
                BuildH4NonStationaritySamples(studyASessions));
        }

        private static EvaluationConfirmatoryResult BuildInference(
            string hypothesis,
            EvaluationPairedSamples samples)
        {
            return new EvaluationConfirmatoryResult(
                hypothesis,
                EvaluationStatistics.PairedMeanDifference(
                    samples.Left,
                    samples.Right),
                EvaluationStatistics.PairedSignFlipMeanDifference(
                    samples.Left,
                    samples.Right));
        }

        private static EvaluationPairedSamples BuildMatchedArchetypeSamples(
            IReadOnlyList<EvaluationSessionSummary> sessions,
            SyntheticArchetypeId leftArchetype,
            SyntheticArchetypeId rightArchetype,
            EvaluationPolicyCondition policy)
        {
            if (sessions == null)
            {
                throw new ArgumentNullException(nameof(sessions));
            }

            var left = new List<double>();
            var right = new List<double>();
            for (var seed = EvaluationExperimentRunner.StudyAFirstSeed;
                seed < EvaluationExperimentRunner.StudyAFirstSeed
                    + EvaluationExperimentRunner.StudyAReplicationCount;
                seed++)
            {
                if (TryFindMeanRegret(
                        sessions,
                        "study-a",
                        leftArchetype,
                        "R0",
                        policy,
                        seed,
                        out var leftValue)
                    && TryFindMeanRegret(
                        sessions,
                        "study-a",
                        rightArchetype,
                        "R0",
                        policy,
                        seed,
                        out var rightValue))
                {
                    left.Add(leftValue);
                    right.Add(rightValue);
                }
            }

            return new EvaluationPairedSamples(
                left.ToArray(),
                right.ToArray());
        }

        private static bool TryFindMeanRegret(
            IReadOnlyList<EvaluationSessionSummary> sessions,
            string experimentId,
            SyntheticArchetypeId archetype,
            string scenarioCode,
            EvaluationPolicyCondition policy,
            int seed,
            out double value)
        {
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                if (session.ExperimentId == experimentId
                    && session.Archetype == archetype
                    && session.ScenarioCode == scenarioCode
                    && session.PolicyCondition == policy
                    && session.Seed == seed
                    && session.MeanPseudoRegret.HasValue)
                {
                    value = session.MeanPseudoRegret.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static Dictionary<int, double> CollectBySeed(
            IReadOnlyList<EvaluationSessionSummary> sessions,
            string experimentId,
            SyntheticArchetypeId archetype,
            string scenarioCode,
            EvaluationPolicyCondition policy)
        {
            var values = new Dictionary<int, double>();
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                if (session.ExperimentId == experimentId
                    && session.Archetype == archetype
                    && session.ScenarioCode == scenarioCode
                    && session.PolicyCondition == policy
                    && session.MeanPseudoRegret.HasValue)
                {
                    values.Add(
                        session.Seed,
                        session.MeanPseudoRegret.Value);
                }
            }

            return values;
        }

        private static int CompareRows(
            EvaluationAggregateRow left,
            EvaluationAggregateRow right)
        {
            var comparison = string.CompareOrdinal(
                left.Key.ExperimentId,
                right.Key.ExperimentId);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Key.Archetype.CompareTo(right.Key.Archetype);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = string.CompareOrdinal(
                left.Key.ScenarioCode,
                right.Key.ScenarioCode);
            return comparison != 0
                ? comparison
                : left.Key.Policy.CompareTo(right.Key.Policy);
        }
    }
}
