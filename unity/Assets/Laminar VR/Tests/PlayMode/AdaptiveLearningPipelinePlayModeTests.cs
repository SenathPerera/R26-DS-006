using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LaminarVR.AdaptiveMeditation.Tests.PlayMode
{
    public sealed class AdaptiveLearningPipelinePlayModeTests
    {
        private readonly List<GameObject> createdObjects =
            new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (var index = 0; index < createdObjects.Count; index++)
            {
                if (createdObjects[index] != null)
                {
                    UnityEngine.Object.Destroy(createdObjects[index]);
                }
            }

            createdObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CompleteActionResponseCycle_UpdatesBanditAfterReward()
        {
            var harness = CreateHarness(StudyPolicyMode.ContextualBandit);
            var opportunity = AdvanceToFirstDecision(harness);
            var decisionTask = harness.Controller.ProcessDecisionAsync(
                "playmode-cycle",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);
            yield return null;
            var decision = Completed(decisionTask);
            Assert.That(decision.ResultCode,
                Is.EqualTo(PolicyDecisionCycleResultCode.RewardWindowOpened));
            Assert.That(
                harness.Controller.Policy.CaptureState().ModelUpdateCount,
                Is.Zero);

            harness.Session.AdvanceTo(8d);
            Assert.That(
                harness.PhysiologyBuffer.Ingest(
                    CreateWindow(1181d, 2.2d),
                    1181d,
                    8d).Accepted,
                Is.True);
            var rewardTask = harness.Controller.TryResolveRewardAsync(
                8d,
                1181d,
                harness.Session.ActiveSessionElapsedSeconds,
                harness.Session.Phase,
                true,
                0d,
                0d,
                CancellationToken.None);
            yield return null;
            var reward = Completed(rewardTask);

            Assert.That(reward.ResultCode,
                Is.EqualTo(PolicyRewardCycleResultCode.RewardApplied));
            Assert.That(
                harness.Controller.Policy.CaptureState().ModelUpdateCount,
                Is.EqualTo(1L));
            Assert.That(harness.Sink.Contains(TelemetryEventTypes.BanditUpdated),
                Is.True);
        }

        [UnityTest]
        public IEnumerator PauseDuringPendingReward_InvalidatesWithoutUpdate()
        {
            var harness = CreateHarness(StudyPolicyMode.ContextualBandit);
            var opportunity = AdvanceToFirstDecision(harness);
            var decisionTask = harness.Controller.ProcessDecisionAsync(
                "playmode-pause",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);
            yield return null;
            Assert.That(Completed(decisionTask).ResultCode,
                Is.EqualTo(PolicyDecisionCycleResultCode.RewardWindowOpened));

            var rewardTask = harness.Controller.TryResolveRewardAsync(
                7.5d,
                1120.5d,
                7.5d,
                VrSessionPhase.Paused,
                true,
                0d,
                0d,
                CancellationToken.None);
            yield return null;
            var reward = Completed(rewardTask);

            Assert.That(reward.ResultCode,
                Is.EqualTo(PolicyRewardCycleResultCode.AttributionInvalidated));
            Assert.That(harness.Controller.HasPendingOutcome, Is.False);
            Assert.That(
                harness.Controller.Policy.CaptureState().ModelUpdateCount,
                Is.Zero);
            Assert.That(
                harness.Sink.Contains(TelemetryEventTypes.BanditUpdateSkipped),
                Is.True);
        }

        [UnityTest]
        public IEnumerator EmergencyDuringTransition_CancelsAndFreezesState()
        {
            var harness = CreateHarness(StudyPolicyMode.RuleBasedAdaptive);
            var opportunity = AdvanceToFirstDecision(harness);
            var decisionTask = harness.Controller.ProcessDecisionAsync(
                "playmode-emergency",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);
            yield return null;
            Assert.That(Completed(decisionTask).ResultCode,
                Is.EqualTo(PolicyDecisionCycleResultCode.TransitionStarted));

            var stateBeforeEmergency =
                harness.EnvironmentManager.CurrentState;
            var invalidationTask = harness.Controller.InvalidatePendingAsync(
                RewardAttributionInvalidationReason.EmergencyStop,
                7.5d,
                1120.5d,
                7.5d,
                CancellationToken.None);
            yield return null;

            Assert.That(Completed(invalidationTask), Is.True);
            Assert.That(harness.EnvironmentManager.IsTransitionActive, Is.False);
            Assert.That(harness.EnvironmentManager.CurrentState,
                Is.EqualTo(stateBeforeEmergency));
            Assert.That(
                harness.SceneAdapter.LastAppliedState,
                Is.EqualTo(stateBeforeEmergency));
        }

        [UnityTest]
        public IEnumerator NetworkAndStalePhysiology_FreezeNewDecisions()
        {
            var harness = CreateHarness(StudyPolicyMode.ContextualBandit);
            var firstOpportunity = AdvanceToFirstDecision(harness);
            var stateBefore = harness.EnvironmentManager.CurrentState;
            var offlineTask = harness.Controller.ProcessDecisionAsync(
                "playmode-offline",
                firstOpportunity,
                harness.Session.Phase,
                false,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);
            yield return null;
            Assert.That(Completed(offlineTask).ResultCode,
                Is.EqualTo(
                    PolicyDecisionCycleResultCode.SkippedNetworkUnavailable));

            harness.Session.AdvanceTo(30d);
            var lastOpportunity = harness.Opportunities[
                harness.Opportunities.Count - 1];
            var staleTask = harness.Controller.ProcessDecisionAsync(
                "playmode-stale",
                lastOpportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1200d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);
            yield return null;
            var stale = Completed(staleTask);

            Assert.That(stale.ResultCode,
                Is.EqualTo(
                    PolicyDecisionCycleResultCode.SkippedPhysiologyUnavailable));
            Assert.That(stale.PhysiologyQueryResult,
                Is.EqualTo(PhysiologyQueryResultCode.Stale));
            Assert.That(harness.EnvironmentManager.CurrentState,
                Is.EqualTo(stateBefore));
            Assert.That(harness.EnvironmentManager.IsTransitionActive, Is.False);
            Assert.That(
                harness.Controller.Policy.CaptureState().DecisionCount,
                Is.Zero);
        }

        [UnityTest]
        public IEnumerator BootstrapPolicySelection_CreatesAllStudyPolicies()
        {
            var builder = new PolicyFeatureVectorBuilder();
            var linUcb = CreateLinUcbConfiguration(builder);
            var rules = CreateRuleConfiguration();

            Assert.That(
                StudyPolicyFactory.TryCreate(
                    StudyPolicyMode.StaticPersonalized,
                    rules,
                    linUcb,
                    builder,
                    out var staticPolicy,
                    out var staticResult),
                Is.True);
            Assert.That(staticResult,
                Is.EqualTo(StudyPolicyCreationResultCode.Created));
            Assert.That(staticPolicy.PolicyId,
                Is.EqualTo("StaticPersonalizedPolicy"));

            Assert.That(
                StudyPolicyFactory.TryCreate(
                    StudyPolicyMode.RuleBasedAdaptive,
                    rules,
                    linUcb,
                    builder,
                    out var rulePolicy,
                    out var ruleResult),
                Is.True);
            Assert.That(ruleResult,
                Is.EqualTo(StudyPolicyCreationResultCode.Created));
            Assert.That(rulePolicy.PolicyId,
                Is.EqualTo("RuleBasedAdaptivePolicy"));

            Assert.That(
                StudyPolicyFactory.TryCreate(
                    StudyPolicyMode.ContextualBandit,
                    rules,
                    linUcb,
                    builder,
                    out var banditPolicy,
                    out var banditResult),
                Is.True);
            Assert.That(banditResult,
                Is.EqualTo(StudyPolicyCreationResultCode.Created));
            Assert.That(banditPolicy,
                Is.TypeOf<ContextualBanditPolicy>());
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneAdapterIntegration_AppliesSmoothTransitionPerFrame()
        {
            var root = CreateRoot("SceneAdapterIntegration");
            var adapter = root.AddComponent<RecordingSceneAdapterBehaviour>();
            var start = State(0.5f);
            var target = new EnvironmentState(
                0.6f,
                0.5f,
                0.5f,
                0.5f,
                0.5f);
            var manager = new EnvironmentParameterManager(start, adapter);
            manager.BeginTransition("adapter-transition", target, 0d, 1d);

            yield return null;
            var halfway = manager.AdvanceTransition(0.5d);
            yield return null;
            var completed = manager.AdvanceTransition(1d);

            Assert.That(halfway.Status,
                Is.EqualTo(EnvironmentTransitionStatus.InProgress));
            Assert.That(halfway.State.Illumination,
                Is.EqualTo(0.55f).Within(1e-6f));
            Assert.That(completed.Status,
                Is.EqualTo(EnvironmentTransitionStatus.Completed));
            Assert.That(adapter.ApplyCount, Is.EqualTo(3));
            Assert.That(adapter.LastAppliedState, Is.EqualTo(target));
        }

        private Harness CreateHarness(StudyPolicyMode policyMode)
        {
            var root = CreateRoot("AdaptiveLearningPlayModeHarness");
            var adapter = root.AddComponent<RecordingSceneAdapterBehaviour>();
            var builder = new PolicyFeatureVectorBuilder();
            Assert.That(
                StudyPolicyFactory.TryCreate(
                    policyMode,
                    CreateRuleConfiguration(),
                    CreateLinUcbConfiguration(builder),
                    builder,
                    out var policy,
                    out var creationResult),
                Is.True,
                creationResult.ToString());

            var physiology = new PhysiologyStateBuffer(
                new PhysiologyValidationConfiguration(
                    "playmode-physiology",
                    1,
                    20d,
                    1d,
                    0d,
                    0d,
                    0.01d,
                    0.8d,
                    0.8d,
                    8));
            var sceneProfile = CreateSceneProfile();
            var environment = new EnvironmentParameterManager(
                sceneProfile.SafeDefault,
                adapter);
            var sink = new RecordingSink();
            var controller = new PolicyController(
                policy,
                new ActionSafetyValidator(),
                environment,
                sceneProfile,
                new ActionSafetyLimits(3, 1d),
                physiology,
                CreateRewardConfiguration(),
                CreateBaseline(),
                new TelemetryRecorder(
                    new TelemetryLoggingConfiguration(
                        "playmode-logging",
                        1,
                        "playmode-event",
                        "1",
                        1),
                    new TelemetrySessionIdentity(
                        "playmode-session",
                        "P-PLAYMODE"),
                    sink,
                    () => Guid.NewGuid().ToString("N")));
            var session = new SessionStateMachine();
            var opportunities = new List<SessionDecisionOpportunity>();
            session.DecisionOpportunityReached += opportunities.Add;
            return new Harness(
                controller,
                environment,
                physiology,
                session,
                opportunities,
                adapter,
                sink);
        }

        private GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            createdObjects.Add(root);
            return root;
        }

        private static SessionDecisionOpportunity AdvanceToFirstDecision(
            Harness harness)
        {
            Assert.That(harness.Session.Initialize(0d), Is.True);
            Assert.That(
                harness.Session.AcceptConfiguration(
                    new SessionTimingConfiguration(
                        "playmode-timing",
                        1,
                        2d,
                        60d,
                        2d,
                        5d),
                    0d),
                Is.True);
            Assert.That(harness.Session.MarkSceneLoaded(0d), Is.True);
            Assert.That(
                harness.Session.ProcessCommand(
                    "start",
                    SessionCommandType.Start,
                    0d,
                    false).Applied,
                Is.True);
            Assert.That(
                harness.PhysiologyBuffer.Ingest(
                    CreateWindow(1000d, 1d),
                    1000d,
                    4d).Accepted,
                Is.True);
            Assert.That(
                harness.PhysiologyBuffer.Ingest(
                    CreateWindow(1060d, 2d),
                    1060d,
                    5d).Accepted,
                Is.True);
            Assert.That(
                harness.PhysiologyBuffer.Ingest(
                    CreateWindow(1120d, 3d),
                    1120d,
                    6d).Accepted,
                Is.True);
            harness.Session.AdvanceTo(7d);
            Assert.That(harness.Opportunities, Has.Count.EqualTo(1));
            return harness.Opportunities[0];
        }

        private static RuleBasedPolicyConfiguration CreateRuleConfiguration()
        {
            return new RuleBasedPolicyConfiguration(
                "playmode-rules",
                1,
                RuleActivationMode.WorseningStressTrend,
                2d,
                0.1d,
                0.05d);
        }

        private static LinUcbModelConfiguration CreateLinUcbConfiguration(
            IFeatureVectorBuilder builder)
        {
            return new LinUcbModelConfiguration(
                "playmode-linucb",
                1,
                builder.FeatureSchemaVersion,
                builder.FeatureCount,
                1d,
                0.1d);
        }

        private static SceneEnvironmentProfile CreateSceneProfile()
        {
            var range = new NormalizedRange(0.2f, 0.8f);
            return new SceneEnvironmentProfile(
                "playmode-scene",
                "PlayMode Scene",
                State(0.5f),
                new EnvironmentStateLimits(
                    range,
                    range,
                    range,
                    range,
                    range),
                0.1f,
                2f,
                0f);
        }

        private static RewardPipelineConfiguration CreateRewardConfiguration()
        {
            return new RewardPipelineConfiguration(
                "playmode-reward",
                1,
                BaselineStandardDeviationMethod.Population,
                3,
                0.01d,
                3,
                3,
                1d,
                10d,
                1d,
                0d,
                0d,
                0.1d,
                1d,
                1d);
        }

        private static PhysiologyBaseline CreateBaseline()
        {
            return new PhysiologyBaseline(
                BaselineStandardDeviationMethod.Population,
                new PhysiologyMetricStatistics(3, 2d, 0.5d),
                new PhysiologyMetricStatistics(3, 75d, 2d),
                new PhysiologyMetricStatistics(3, 35d, 2d));
        }

        private static PhysiologyWindow CreateWindow(
            double windowEndUtcUnixSeconds,
            double stressScore)
        {
            return new PhysiologyWindow(
                windowEndUtcUnixSeconds,
                windowEndUtcUnixSeconds - 60d,
                windowEndUtcUnixSeconds,
                75d,
                35d,
                40d,
                new StressDecision(
                    StressDecisionMode.Point,
                    2,
                    null,
                    null,
                    "moderate",
                    0.7d,
                    false,
                    new StressProbabilityVector(0.1d, 0.2d, 0.6d, 0.1d),
                    stressScore),
                0.95d);
        }

        private static EnvironmentState PreferredEnvironment()
        {
            return new EnvironmentState(0.5f, 0.8f, 0.5f, 0.5f, 0.5f);
        }

        private static EnvironmentState State(float value)
        {
            return new EnvironmentState(value, value, value, value, value);
        }

        private static T Completed<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True,
                "The operation did not complete within the PlayMode frame.");
            return task.GetAwaiter().GetResult();
        }

        private sealed class Harness
        {
            public Harness(
                PolicyController controller,
                EnvironmentParameterManager environmentManager,
                PhysiologyStateBuffer physiologyBuffer,
                SessionStateMachine session,
                List<SessionDecisionOpportunity> opportunities,
                RecordingSceneAdapterBehaviour sceneAdapter,
                RecordingSink sink)
            {
                Controller = controller;
                EnvironmentManager = environmentManager;
                PhysiologyBuffer = physiologyBuffer;
                Session = session;
                Opportunities = opportunities;
                SceneAdapter = sceneAdapter;
                Sink = sink;
            }

            public PolicyController Controller { get; }
            public EnvironmentParameterManager EnvironmentManager { get; }
            public PhysiologyStateBuffer PhysiologyBuffer { get; }
            public SessionStateMachine Session { get; }
            public List<SessionDecisionOpportunity> Opportunities { get; }
            public RecordingSceneAdapterBehaviour SceneAdapter { get; }
            public RecordingSink Sink { get; }
        }

        private sealed class RecordingSink : ITelemetryEventSink
        {
            private readonly List<string> eventTypes = new List<string>();

            public bool Contains(string eventType)
            {
                return eventTypes.Contains(eventType);
            }

            public Task AppendAsync(
                TelemetryEvent telemetryEvent,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                eventTypes.Add(telemetryEvent.EventType);
                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}
