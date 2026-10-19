using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Safety;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public enum LongitudinalPolicyCondition
    {
        CarryLinUcb,
        ResetLinUcb,
        StaticPersonalized,
        RuleBasedAdaptive
    }

    public enum LongitudinalScenario
    {
        Primary,
        BetweenSessionDrift,
        MildPhysiologyLoss,
        NonResponder
    }

    public interface ILongitudinalResultSink
    {
        void OnProbe(
            LongitudinalScenario scenario,
            SyntheticArchetypeId archetype,
            int profileSeed,
            LongitudinalPolicyCondition condition,
            int checkpoint,
            int probeIndex,
            SafeOracleDecisionRecord result,
            long modelUpdateCount);

        void OnSession(
            LongitudinalScenario scenario,
            SyntheticArchetypeId archetype,
            int profileSeed,
            int sessionIndex,
            int sessionSeed,
            LongitudinalPolicyCondition condition,
            EvaluationSessionSummary summary,
            EvaluationSessionResult result,
            long priorModelUpdateCount);
    }

    /// <summary>
    /// Evaluation-only repeated sessions. The Quest production coordinator
    /// keeps its existing per-session model reset.
    /// </summary>
    public sealed class LongitudinalStudyRunner
    {
        public const int PrimaryFirstSeed = 410000;
        public const int ChallengeFirstSeed = 420000;
        public const int DevelopmentPrimaryFirstSeed = 510000;
        public const int DevelopmentChallengeFirstSeed = 520000;
        public const int ProfileReplications = 30;
        public const int SessionsPerProfile = 20;
        public const int ProbesPerCheckpoint = 32;
        public const int FullStudySessionCount = 20400;

        private static readonly LongitudinalPolicyCondition[] PrimaryPolicies =
        {
            LongitudinalPolicyCondition.CarryLinUcb,
            LongitudinalPolicyCondition.ResetLinUcb,
            LongitudinalPolicyCondition.StaticPersonalized,
            LongitudinalPolicyCondition.RuleBasedAdaptive
        };

        private static readonly LongitudinalPolicyCondition[] ChallengePolicies =
        {
            LongitudinalPolicyCondition.CarryLinUcb,
            LongitudinalPolicyCondition.ResetLinUcb
        };

        public async Task<int> RunAsync(
            EvaluationRuntimeBundle bundle,
            ILongitudinalResultSink sink,
            Action<int> reportCompleted,
            CancellationToken cancellationToken,
            int replications = ProfileReplications,
            int sessionCount = SessionsPerProfile,
            int probeCount = ProbesPerCheckpoint,
            bool useDevelopmentSeeds = false)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (replications < 1 || replications > ProfileReplications
                || sessionCount < 1 || sessionCount > SessionsPerProfile
                || probeCount < 1 || probeCount > ProbesPerCheckpoint)
            {
                throw new ArgumentOutOfRangeException(
                    "Study dimensions must be within the frozen matrix.");
            }

            var completed = 0;
            var primaryFirstSeed = useDevelopmentSeeds
                ? DevelopmentPrimaryFirstSeed : PrimaryFirstSeed;
            var challengeFirstSeed = useDevelopmentSeeds
                ? DevelopmentChallengeFirstSeed : ChallengeFirstSeed;
            for (var archetypeValue = (int)SyntheticArchetypeId.A1PreferenceAlignedBalanced;
                archetypeValue <= (int)SyntheticArchetypeId.A7WeakResponder;
                archetypeValue++)
            {
                for (var offset = 0; offset < replications; offset++)
                {
                    foreach (var condition in PrimaryPolicies)
                    {
                        await RunTrajectoryAsync(
                            bundle, sink, LongitudinalScenario.Primary,
                            (SyntheticArchetypeId)archetypeValue,
                            primaryFirstSeed + offset, condition,
                            sessionCount, probeCount, cancellationToken,
                            () =>
                            {
                                completed++;
                                reportCompleted?.Invoke(completed);
                            })
                            .ConfigureAwait(false);
                    }
                }
            }

            var challenges = new[]
            {
                LongitudinalScenario.BetweenSessionDrift,
                LongitudinalScenario.MildPhysiologyLoss,
                LongitudinalScenario.NonResponder
            };
            foreach (var scenario in challenges)
            {
                var archetype = scenario == LongitudinalScenario.NonResponder
                    ? SyntheticArchetypeId.A9PhysiologicalNonResponder
                    : SyntheticArchetypeId.A1PreferenceAlignedBalanced;
                for (var offset = 0; offset < replications; offset++)
                {
                    foreach (var condition in ChallengePolicies)
                    {
                        await RunTrajectoryAsync(
                            bundle, sink, scenario, archetype,
                            challengeFirstSeed + offset, condition,
                            sessionCount, probeCount, cancellationToken,
                            () =>
                            {
                                completed++;
                                reportCompleted?.Invoke(completed);
                            })
                            .ConfigureAwait(false);
                    }
                }
            }

            return completed;
        }

        private static async Task RunTrajectoryAsync(
            EvaluationRuntimeBundle bundle,
            ILongitudinalResultSink sink,
            LongitudinalScenario scenario,
            SyntheticArchetypeId archetype,
            int profileSeed,
            LongitudinalPolicyCondition condition,
            int sessionCount,
            int probeCount,
            CancellationToken cancellationToken,
            Action reportCompleted)
        {
            var profile = SyntheticReplicationFactory.Create(profileSeed, archetype);
            var pseudonym = "synthetic-longitudinal-"
                + scenario + "-" + archetype + "-" + profileSeed;
            LinUcbModelSnapshot carriedSnapshot = null;
            var probeEvaluator = new LongitudinalProbeEvaluator(
                bundle, profile, pseudonym, probeCount);
            probeEvaluator.Evaluate(
                scenario, condition, profileSeed, 0, null, sink);

            for (var sessionIndex = 1; sessionIndex <= sessionCount; sessionIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sessionSeed = checked(profileSeed * 100 + sessionIndex);
                var sessionReplication = SyntheticReplicationFactory.Create(
                    sessionSeed, archetype);
                var responsePreference = ResponsePreference(
                    profile.Preference, scenario, sessionIndex);
                var fault = scenario == LongitudinalScenario.MildPhysiologyLoss
                    ? SyntheticFaultScenarioId.R3MildLoss
                    : SyntheticFaultScenarioId.R0Nominal;
                var faultDefinition = SyntheticFaultScenarioCatalog.Get(fault);
                var model = new ProtocolSyntheticPhysiologyModel(
                    profile.Archetype,
                    profile.Preference,
                    sessionReplication.SessionParameters,
                    sessionReplication.CreateRandomStream(
                        EvaluationRandomStreamId.PhysiologyNoise),
                    faultDefinition.NoiseMultiplier,
                    responsePreference);
                ISyntheticPhysiologyModel physiology = fault
                        == SyntheticFaultScenarioId.R0Nominal
                    ? (ISyntheticPhysiologyModel)model
                    : new FaultInjectingPhysiologyModel(
                        model,
                        faultDefinition,
                        sessionReplication.CreateRandomStream(
                            EvaluationRandomStreamId.MissingWindowSchedule));
                var prior = condition == LongitudinalPolicyCondition.CarryLinUcb
                    ? carriedSnapshot
                    : null;
                var priorUpdates = prior?.TotalUpdateCount ?? 0L;
                var sessionId = "longitudinal-" + scenario + "-"
                    + archetype + "-" + profileSeed + "-"
                    + condition + "-s" + sessionIndex.ToString("D2");
                var request = new EvaluationSessionRequest(
                    bundle,
                    sessionId,
                    pseudonym,
                    EvaluationCondition(condition),
                    profile.Preference,
                    physiology,
                    EvaluationExperimentRunner.BaseUtcUnixSeconds
                        + (sessionIndex * 86400d),
                    faultDefinition.CreateNetworkSchedule(),
                    null,
                    prior,
                    LongitudinalEvaluationProfileLoader.ProtocolId,
                    LongitudinalEvaluationProfileLoader.PilotBundleId);
                var result = await new EvaluationSessionRunner().RunAsync(
                    request, cancellationToken).ConfigureAwait(false);
                var summary = EvaluationMetricCalculator.Calculate(
                    "longitudinal-" + scenario,
                    archetype,
                    faultDefinition.Code,
                    EvaluationCondition(condition),
                    profileSeed,
                    bundle,
                    result,
                    priorUpdates);
                VerifySession(result, summary, condition, priorUpdates);
                sink.OnSession(
                    scenario, archetype, profileSeed, sessionIndex,
                    sessionSeed, condition, summary, result, priorUpdates);
                if (condition == LongitudinalPolicyCondition.CarryLinUcb)
                {
                    carriedSnapshot = result.ModelSnapshot;
                }

                probeEvaluator.Evaluate(
                    scenario, condition, profileSeed, sessionIndex,
                    result.ModelSnapshot, sink);
                reportCompleted?.Invoke();
            }
        }

        private static void VerifySession(
            EvaluationSessionResult result,
            EvaluationSessionSummary summary,
            LongitudinalPolicyCondition condition,
            long priorUpdates)
        {
            if (!result.AllAppliedStatesSafe
                || summary.SceneLimitViolationCount != 0
                || summary.MultiDimensionViolationCount != 0
                || summary.InvalidModelUpdateCount != 0)
            {
                throw new InvalidOperationException(
                    "A longitudinal safety or attribution invariant failed.");
            }

            if (condition == LongitudinalPolicyCondition.CarryLinUcb
                || condition == LongitudinalPolicyCondition.ResetLinUcb)
            {
                if (result.ModelSnapshot == null
                    || result.ModelSnapshot.TotalUpdateCount - priorUpdates
                        != result.RewardAppliedCount)
                {
                    throw new InvalidOperationException(
                        "Longitudinal model updates do not match valid rewards.");
                }
            }
        }

        public static EnvironmentState ResponsePreference(
            EnvironmentState preference,
            LongitudinalScenario scenario,
            int sessionIndex)
        {
            if (scenario != LongitudinalScenario.BetweenSessionDrift
                || sessionIndex <= 10)
            {
                return preference;
            }

            return new EnvironmentState(
                Invert(preference.Illumination),
                Invert(preference.Warmth),
                preference.AtmosphericSoftness,
                preference.ColorRichness,
                preference.AmbientMotion);
        }

        private static float Invert(float value)
        {
            return Math.Min(0.8f, Math.Max(0.2f, 1f - value));
        }

        private static EvaluationPolicyCondition EvaluationCondition(
            LongitudinalPolicyCondition condition)
        {
            switch (condition)
            {
                case LongitudinalPolicyCondition.CarryLinUcb:
                case LongitudinalPolicyCondition.ResetLinUcb:
                    return EvaluationPolicyCondition.ContextualBandit;
                case LongitudinalPolicyCondition.StaticPersonalized:
                    return EvaluationPolicyCondition.StaticPersonalized;
                case LongitudinalPolicyCondition.RuleBasedAdaptive:
                    return EvaluationPolicyCondition.RuleBasedAdaptive;
                default:
                    throw new ArgumentOutOfRangeException(nameof(condition));
            }
        }
    }

    internal sealed class LongitudinalProbeEvaluator
    {
        private readonly EvaluationRuntimeBundle bundle;
        private readonly SyntheticReplication profile;
        private readonly string pseudonym;
        private readonly int probeCount;
        private readonly ActionSafetyValidator safety = new ActionSafetyValidator();

        public LongitudinalProbeEvaluator(
            EvaluationRuntimeBundle bundle,
            SyntheticReplication profile,
            string pseudonym,
            int probeCount)
        {
            this.bundle = bundle;
            this.profile = profile;
            this.pseudonym = pseudonym;
            this.probeCount = probeCount;
        }

        public void Evaluate(
            LongitudinalScenario scenario,
            LongitudinalPolicyCondition condition,
            int profileSeed,
            int checkpoint,
            LinUcbModelSnapshot snapshot,
            ILongitudinalResultSink sink)
        {
            var responsePreference = LongitudinalStudyRunner.ResponsePreference(
                profile.Preference, scenario, Math.Max(1, checkpoint));
            var model = new ProtocolSyntheticPhysiologyModel(
                profile.Archetype,
                profile.Preference,
                profile.SessionParameters,
                profile.CreateRandomStream(
                    EvaluationRandomStreamId.PhysiologyNoise),
                1d,
                responsePreference);
            var oracle = new SafeOracleEvaluator(bundle.Scene, model);
            var seed = checked(profileSeed * 100
                + (scenario == LongitudinalScenario.BetweenSessionDrift
                    && checkpoint >= 11 ? 98 : 99));
            var random = EvaluationRandomStreams.Create(
                seed, EvaluationRandomStreamId.LongitudinalProbeContexts);
            IEnvironmentPolicy policy = condition
                    == LongitudinalPolicyCondition.StaticPersonalized
                ? bundle.CreatePolicy(StudyPolicyMode.StaticPersonalized)
                : condition == LongitudinalPolicyCondition.RuleBasedAdaptive
                    ? bundle.CreatePolicy(StudyPolicyMode.RuleBasedAdaptive)
                    : bundle.CreatePolicy(StudyPolicyMode.ContextualBandit);
            policy.Reset(new PolicyResetContext(PolicyResetReason.NewSession));
            if (snapshot != null)
            {
                var restore = ((ContextualBanditPolicy)policy)
                    .TryRestoreModelSnapshot(snapshot, pseudonym);
                if (!restore.Restored)
                {
                    throw new InvalidOperationException(
                        "Probe snapshot restore failed: " + restore.Code);
                }
            }

            for (var probeIndex = 0; probeIndex < probeCount; probeIndex++)
            {
                var observation = CreateProbe(random, probeIndex);
                var chosen = policy.SelectAction(observation).SelectedAction;
                var validation = safety.Validate(
                    chosen,
                    observation.CurrentEnvironment,
                    bundle.Scene,
                    SafetyRuntimeState.Ready,
                    bundle.Coordinator.SafetyLimits);
                var executed = validation.Accepted
                    ? validation.ExecutedAction
                    : EnvironmentAction.NoChange;
                var result = oracle.Evaluate(
                    observation, chosen, executed, 195d);
                sink.OnProbe(
                    scenario, profile.Archetype.Id, profileSeed,
                    condition, checkpoint, probeIndex, result,
                    snapshot?.TotalUpdateCount ?? 0L);
            }
        }

        private PolicyObservation CreateProbe(Pcg32 random, int index)
        {
            var state = new EnvironmentState(
                (float)ProbeValue(random, index, 0),
                (float)ProbeValue(random, index, 1),
                (float)ProbeValue(random, index, 2),
                (float)ProbeValue(random, index, 3),
                (float)ProbeValue(random, index, 4));
            var candidates = new List<PolicyActionCandidate>();
            for (var actionValue = (int)EnvironmentAction.NoChange;
                actionValue <= (int)EnvironmentAction.DecreaseAmbientMotion;
                actionValue++)
            {
                var action = (EnvironmentAction)actionValue;
                var validation = safety.Validate(
                    action, state, bundle.Scene, SafetyRuntimeState.Ready,
                    bundle.Coordinator.SafetyLimits);
                if (validation.Accepted)
                {
                    candidates.Add(new PolicyActionCandidate(
                        action, validation.AppliedVariation));
                }
            }

            var stress = random.NextDouble(0.3d, 2.8d);
            var end = EvaluationExperimentRunner.BaseUtcUnixSeconds + 195d;
            var window = new PhysiologyWindow(
                end, end - 30d, end,
                random.NextDouble(60d, 85d),
                random.NextDouble(25d, 65d),
                null,
                SyntheticStressDecisionFactory.FromContinuousScore(stress),
                ProtocolSyntheticPhysiologyModel.NominalSignalQuality);
            return new PolicyObservation(
                new PhysiologyWindowSnapshot(1L, window, 0d, 195d),
                profile.Preference,
                state,
                bundle.Scene.SafeDefault,
                null,
                candidates);
        }

        private static double ProbeValue(Pcg32 random, int index, int dimension)
        {
            if (index < 5 && dimension == index)
            {
                return index % 2 == 0 ? 0.05d : 0.95d;
            }

            return random.NextDouble(0.05d, 0.95d);
        }
    }
}
