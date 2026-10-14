using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public enum EvaluationStudyId
    {
        StudyAPrimaryPolicyComparison = 1,
        StudyBRobustness = 2
    }

    public readonly struct EvaluationRunProgress
    {
        public EvaluationRunProgress(
            EvaluationStudyId study,
            int completedSessions,
            int totalSessions)
        {
            Study = study;
            CompletedSessions = completedSessions;
            TotalSessions = totalSessions;
        }

        public EvaluationStudyId Study { get; }
        public int CompletedSessions { get; }
        public int TotalSessions { get; }
    }

    public sealed class EvaluationStudyResult
    {
        public EvaluationStudyResult(
            EvaluationStudyId study,
            EvaluationSessionSummary[] sessions)
        {
            Study = study;
            Sessions = sessions ?? throw new ArgumentNullException(
                nameof(sessions));
        }

        public EvaluationStudyId Study { get; }
        public EvaluationSessionSummary[] Sessions { get; }
    }

    public sealed class EvaluationCellResult
    {
        public EvaluationCellResult(
            EvaluationSessionSummary summary,
            EvaluationSessionResult session)
        {
            Summary = summary ?? throw new ArgumentNullException(
                nameof(summary));
            Session = session ?? throw new ArgumentNullException(
                nameof(session));
        }

        public EvaluationSessionSummary Summary { get; }
        public EvaluationSessionResult Session { get; }
    }

    public interface IEvaluationSessionSink
    {
        Task OnSessionCompletedAsync(
            EvaluationCellResult cell,
            CancellationToken cancellationToken);
    }

    public sealed class EvaluationExperimentRunner
    {
        public const int StudyAFirstSeed = 100000;
        public const int StudyAReplicationCount = 200;
        public const int StudyBFirstSeed = 200000;
        public const int StudyBReplicationCount = 100;
        public const int StudyASessionCount = 7200;
        public const int StudyBSessionCount = 10400;
        public const double BaseUtcUnixSeconds = 1700000000d;

        private static readonly EvaluationPolicyCondition[] Policies =
        {
            EvaluationPolicyCondition.StaticPersonalized,
            EvaluationPolicyCondition.RuleBasedAdaptive,
            EvaluationPolicyCondition.ContextualBandit,
            EvaluationPolicyCondition.RandomSafe
        };

        private static readonly SyntheticArchetypeId[] StudyBArchetypes =
        {
            SyntheticArchetypeId.A1PreferenceAlignedBalanced,
            SyntheticArchetypeId.A5MotionAverseThreshold
        };

        public async Task<EvaluationStudyResult> RunStudyAAsync(
            EvaluationRuntimeBundle bundle,
            IProgress<EvaluationRunProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return await RunStudyAInternalAsync(
                bundle,
                null,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<EvaluationStudyResult> RunStudyAToSinkAsync(
            EvaluationRuntimeBundle bundle,
            IEvaluationSessionSink sink,
            IProgress<EvaluationRunProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            return await RunStudyAInternalAsync(
                bundle,
                sink,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<EvaluationStudyResult> RunStudyAInternalAsync(
            EvaluationRuntimeBundle bundle,
            IEvaluationSessionSink sink,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            RequireBundle(bundle);
            var summaries = new List<EvaluationSessionSummary>(
                StudyASessionCount);
            var completed = 0;
            for (var archetypeIndex = 0;
                archetypeIndex < SyntheticArchetypeCatalog.Count;
                archetypeIndex++)
            {
                var archetype = SyntheticArchetypeCatalog.GetAt(
                    archetypeIndex).Id;
                for (var seedOffset = 0;
                    seedOffset < StudyAReplicationCount;
                    seedOffset++)
                {
                    var seed = StudyAFirstSeed + seedOffset;
                    for (var policyIndex = 0;
                        policyIndex < Policies.Length;
                        policyIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var cell = await RunCellDetailedAsync(
                            bundle,
                            "study-a",
                            archetype,
                            SyntheticFaultScenarioId.R0Nominal,
                            Policies[policyIndex],
                            seed,
                            cancellationToken).ConfigureAwait(false);
                        summaries.Add(cell.Summary);
                        if (sink != null)
                        {
                            await sink.OnSessionCompletedAsync(
                                cell,
                                cancellationToken).ConfigureAwait(false);
                        }
                        completed++;
                        progress?.Report(new EvaluationRunProgress(
                            EvaluationStudyId.StudyAPrimaryPolicyComparison,
                            completed,
                            StudyASessionCount));
                    }
                }
            }

            return new EvaluationStudyResult(
                EvaluationStudyId.StudyAPrimaryPolicyComparison,
                summaries.ToArray());
        }

        public async Task<EvaluationStudyResult> RunStudyBAsync(
            EvaluationRuntimeBundle bundle,
            IProgress<EvaluationRunProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return await RunStudyBInternalAsync(
                bundle,
                null,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<EvaluationStudyResult> RunStudyBToSinkAsync(
            EvaluationRuntimeBundle bundle,
            IEvaluationSessionSink sink,
            IProgress<EvaluationRunProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            return await RunStudyBInternalAsync(
                bundle,
                sink,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<EvaluationStudyResult> RunStudyBInternalAsync(
            EvaluationRuntimeBundle bundle,
            IEvaluationSessionSink sink,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            RequireBundle(bundle);
            var summaries = new List<EvaluationSessionSummary>(
                StudyBSessionCount);
            var completed = 0;
            for (var archetypeIndex = 0;
                archetypeIndex < StudyBArchetypes.Length;
                archetypeIndex++)
            {
                for (var scenarioIndex = 0;
                    scenarioIndex < SyntheticFaultScenarioCatalog.Count;
                    scenarioIndex++)
                {
                    var scenario = SyntheticFaultScenarioCatalog.GetAt(
                        scenarioIndex).Id;
                    for (var seedOffset = 0;
                        seedOffset < StudyBReplicationCount;
                        seedOffset++)
                    {
                        var seed = StudyBFirstSeed + seedOffset;
                        for (var policyIndex = 0;
                            policyIndex < Policies.Length;
                            policyIndex++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var cell = await RunCellDetailedAsync(
                                bundle,
                                "study-b",
                                StudyBArchetypes[archetypeIndex],
                                scenario,
                                Policies[policyIndex],
                                seed,
                                cancellationToken).ConfigureAwait(false);
                            summaries.Add(cell.Summary);
                            if (sink != null)
                            {
                                await sink.OnSessionCompletedAsync(
                                    cell,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            completed++;
                            progress?.Report(new EvaluationRunProgress(
                                EvaluationStudyId.StudyBRobustness,
                                completed,
                                StudyBSessionCount));
                        }
                    }
                }
            }

            return new EvaluationStudyResult(
                EvaluationStudyId.StudyBRobustness,
                summaries.ToArray());
        }

        public async Task<EvaluationSessionSummary> RunCellAsync(
            EvaluationRuntimeBundle bundle,
            string experimentId,
            SyntheticArchetypeId archetype,
            SyntheticFaultScenarioId scenario,
            EvaluationPolicyCondition policy,
            int seed,
            CancellationToken cancellationToken = default)
        {
            var cell = await RunCellDetailedAsync(
                bundle,
                experimentId,
                archetype,
                scenario,
                policy,
                seed,
                cancellationToken).ConfigureAwait(false);
            return cell.Summary;
        }

        public async Task<EvaluationCellResult> RunCellDetailedAsync(
            EvaluationRuntimeBundle bundle,
            string experimentId,
            SyntheticArchetypeId archetype,
            SyntheticFaultScenarioId scenario,
            EvaluationPolicyCondition policy,
            int seed,
            CancellationToken cancellationToken = default)
        {
            RequireBundle(bundle);
            var replication = SyntheticReplicationFactory.Create(
                seed,
                archetype);
            var scenarioDefinition = SyntheticFaultScenarioCatalog.Get(
                scenario);
            var physiology = SyntheticFaultSessionFactory
                .CreatePhysiologyModel(replication, scenario);
            var sessionId = experimentId
                + "-"
                + replication.Archetype.Code
                + "-"
                + scenarioDefinition.Code
                + "-"
                + policy
                + "-"
                + seed;
            var request = new EvaluationSessionRequest(
                bundle,
                sessionId,
                "synthetic-" + seed,
                policy,
                replication.Preference,
                physiology,
                BaseUtcUnixSeconds,
                scenarioDefinition.CreateNetworkSchedule(),
                policy == EvaluationPolicyCondition.RandomSafe
                    ? replication.CreateRandomStream(
                        EvaluationRandomStreamId.RandomSafeActionSelection)
                    : null);
            var result = await new EvaluationSessionRunner().RunAsync(
                request,
                cancellationToken).ConfigureAwait(false);
            var summary = EvaluationMetricCalculator.Calculate(
                experimentId,
                archetype,
                scenarioDefinition.Code,
                policy,
                seed,
                bundle,
                result);
            return new EvaluationCellResult(summary, result);
        }

        private static void RequireBundle(EvaluationRuntimeBundle bundle)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }
        }
    }
}
