using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Runtime.Telemetry;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Stabilization;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class PolicyControllerIntegrationTests
    {
        [TestCase(StudyPolicyMode.StaticPersonalized)]
        [TestCase(StudyPolicyMode.RuleBasedAdaptive)]
        [TestCase(StudyPolicyMode.ContextualBandit)]
        public async Task StudyPolicy_CompletesSameSimulatedSessionPipeline(
            StudyPolicyMode policyMode)
        {
            var harness = CreateHarness(policyMode);
            var opportunity = AdvanceToFirstDecision(harness);

            var decisionResult = await harness.Controller.ProcessDecisionAsync(
                "decision-1",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);

            double rewardMonotonicTime;
            double postWindowEndUtc;
            if (policyMode != StudyPolicyMode.RuleBasedAdaptive)
            {
                Assert.That(
                    decisionResult.ResultCode,
                    Is.EqualTo(
                        PolicyDecisionCycleResultCode.RewardWindowOpened));
                Assert.That(
                    decisionResult.Decision.SelectedAction,
                    Is.EqualTo(EnvironmentAction.NoChange));
                rewardMonotonicTime = 8d;
                postWindowEndUtc = 1181d;
            }
            else
            {
                Assert.That(
                    decisionResult.ResultCode,
                    Is.EqualTo(
                        PolicyDecisionCycleResultCode.TransitionStarted));
                Assert.That(
                    decisionResult.Decision.SelectedAction,
                    Is.EqualTo(EnvironmentAction.IncreaseWarmth));
                harness.Session.AdvanceTo(9d);
                var transition = await harness.Controller
                    .AdvanceTransitionAsync(
                        9d,
                        1122d,
                        harness.Session.ActiveSessionElapsedSeconds,
                        harness.Session.Phase,
                        true,
                        CancellationToken.None);
                Assert.That(
                    transition.Status,
                    Is.EqualTo(EnvironmentTransitionStatus.Completed));
                Assert.That(
                    harness.EnvironmentManager.CurrentState.Warmth,
                    Is.EqualTo(0.6f).Within(1e-6f));
                rewardMonotonicTime = 10d;
                postWindowEndUtc = 1183d;
            }

            harness.Session.AdvanceTo(rewardMonotonicTime);
            var ingestion = harness.PhysiologyBuffer.Ingest(
                CreateWindow(postWindowEndUtc, 2.2d),
                postWindowEndUtc,
                rewardMonotonicTime);
            Assert.That(ingestion.Accepted, Is.True);
            var rewardResult = await harness.Controller.TryResolveRewardAsync(
                rewardMonotonicTime,
                postWindowEndUtc,
                harness.Session.ActiveSessionElapsedSeconds,
                harness.Session.Phase,
                true,
                0d,
                0d,
                CancellationToken.None);

            harness.Session.AdvanceTo(13d);
            var state = harness.Controller.Policy.CaptureState();

            Assert.That(
                rewardResult.ResultCode,
                Is.EqualTo(PolicyRewardCycleResultCode.RewardApplied));
            Assert.That(state.DecisionCount, Is.EqualTo(1L));
            Assert.That(state.ObservedOutcomeCount, Is.EqualTo(1L));
            Assert.That(
                state.ModelUpdateCount,
                Is.EqualTo(
                    policyMode == StudyPolicyMode.ContextualBandit
                        ? 1L
                        : 0L));
            Assert.That(harness.Session.Phase, Is.EqualTo(VrSessionPhase.Completed));
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.DecisionRequested));
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.ActionProposed));
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.ActionValidated));
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.ActionExecuted));
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.RewardWindowOpened));
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.RewardWindowClosed));
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.RewardCalculated));
            Assert.That(
                harness.Sink.EventTypes.Contains(
                    TelemetryEventTypes.BanditUpdated),
                Is.EqualTo(
                    policyMode == StudyPolicyMode.ContextualBandit));
            var validationEvent = harness.Sink.Events.Last(
                telemetryEvent => telemetryEvent.EventType
                    == TelemetryEventTypes.ActionValidated);
            Assert.That(
                validationEvent.CopyFields().Select(field => field.Name),
                Does.Contain("state_before_illumination")
                    .And.Contain("safe_target_ambient_motion"));
            if (policyMode == StudyPolicyMode.ContextualBandit)
            {
                var proposalEvent = harness.Sink.Events.Last(
                    telemetryEvent => telemetryEvent.EventType
                        == TelemetryEventTypes.ActionProposed);
                Assert.That(
                    proposalEvent.CopyFields().Select(field => field.Name),
                    Does.Contain("model_version")
                        .And.Contain("feature_0"));
                var updateEvent = harness.Sink.Events.Last(
                    telemetryEvent => telemetryEvent.EventType
                        == TelemetryEventTypes.BanditUpdated);
                Assert.That(
                    updateEvent.CopyFields().Select(field => field.Name),
                    Does.Contain("model_version")
                        .And.Contain("snapshot_schema_version"));
                var formatter = new JsonLinesTelemetryFormatter();
                var jsonLines = string.Join(
                    "\n",
                    harness.Sink.Events.Select(formatter.Format));
                Assert.That(jsonLines,
                    Does.Contain("\"eventType\":\"decision.requested\"")
                        .And.Contain("\"state_before_illumination\"")
                        .And.Contain("\"feature_0\"")
                        .And.Contain("\"eventType\":\"action.executed\"")
                        .And.Contain("\"total_reward\"")
                        .And.Contain("\"model_update_count\"")
                        .And.Contain("\"snapshot_schema_version\""));
            }
        }

        [Test]
        public async Task Decision_RequiresFreshNetworkedPhysiologyBeforePolicy()
        {
            var harness = CreateHarness(StudyPolicyMode.StaticPersonalized);
            BootstrapSession(harness.Session);
            harness.Session.AdvanceTo(7d);
            var opportunity = harness.Opportunities[0];

            var offline = await harness.Controller.ProcessDecisionAsync(
                "offline",
                opportunity,
                harness.Session.Phase,
                false,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);
            var unavailable = await harness.Controller.ProcessDecisionAsync(
                "no-physiology",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);

            Assert.That(
                offline.ResultCode,
                Is.EqualTo(
                    PolicyDecisionCycleResultCode
                        .SkippedNetworkUnavailable));
            Assert.That(
                unavailable.ResultCode,
                Is.EqualTo(
                    PolicyDecisionCycleResultCode
                        .SkippedPhysiologyUnavailable));
            Assert.That(
                harness.Controller.Policy.CaptureState().DecisionCount,
                Is.Zero);
            Assert.That(harness.EnvironmentManager.IsTransitionActive, Is.False);
            Assert.That(
                harness.Sink.EventTypes.Count(
                    eventType => eventType == TelemetryEventTypes.DecisionSkipped),
                Is.EqualTo(2));
        }

        [Test]
        public async Task LearnedContextualPreference_StillUsesFinalSafetyPipeline()
        {
            var harness = CreateHarness(StudyPolicyMode.ContextualBandit);
            var opportunity = AdvanceToFirstDecision(harness);
            Assert.That(
                harness.PhysiologyBuffer.TryGetLatestAccepted(
                    out var physiology),
                Is.True);
            var builder = new PolicyFeatureVectorBuilder();
            var trainingObservation = new PolicyObservation(
                physiology,
                PreferredEnvironment(),
                harness.EnvironmentManager.CurrentState,
                harness.EnvironmentManager.CurrentState);
            var policy = (ContextualBanditPolicy)harness.Controller.Policy;
            policy.Model.Update(
                EnvironmentAction.IncreaseWarmth,
                builder.Build(trainingObservation),
                2d);

            var result = await harness.Controller.ProcessDecisionAsync(
                "learned-warmth",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);

            Assert.That(
                result.ResultCode,
                Is.EqualTo(PolicyDecisionCycleResultCode.TransitionStarted));
            Assert.That(
                result.Decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.IncreaseWarmth));
            Assert.That(result.Validation.HasValue, Is.True);
            Assert.That(result.Validation.Value.Accepted, Is.True);
            Assert.That(
                result.Validation.Value.ExecutedAction,
                Is.EqualTo(EnvironmentAction.IncreaseWarmth));
            Assert.That(harness.EnvironmentManager.IsTransitionActive, Is.True);
        }

        [Test]
        public async Task ContextualCandidates_RespectSessionSensitivityLimits()
        {
            var boundaryState = new EnvironmentState(
                0.5f,
                0.7f,
                0.5f,
                0.5f,
                0.5f);
            var sceneRange = new NormalizedRange(0.2f, 0.8f);
            var sensitivityLimits = new EnvironmentStateLimits(
                sceneRange,
                new NormalizedRange(0.2f, 0.7f),
                sceneRange,
                sceneRange,
                sceneRange);
            var harness = CreateHarness(
                StudyPolicyMode.ContextualBandit,
                sceneProfileOverride: CreateSceneProfile(boundaryState),
                sessionAdaptationLimits: sensitivityLimits);
            var opportunity = AdvanceToFirstDecision(harness);

            var result = await harness.Controller.ProcessDecisionAsync(
                "warmth-boundary",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);

            Assert.That(result.Decision, Is.Not.Null);
            for (var index = 0;
                index < result.Decision.CandidateScoreCount;
                index++)
            {
                Assert.That(
                    result.Decision.GetCandidateScore(index).Action,
                    Is.Not.EqualTo(EnvironmentAction.IncreaseWarmth));
            }

            Assert.That(
                result.Decision.SelectedAction,
                Is.Not.EqualTo(EnvironmentAction.IncreaseWarmth));
        }

        [Test]
        public async Task EmergencyStop_CancelsTransitionAndInvalidatesOutcome()
        {
            var harness = CreateHarness(StudyPolicyMode.RuleBasedAdaptive);
            var opportunity = AdvanceToFirstDecision(harness);
            var decision = await harness.Controller.ProcessDecisionAsync(
                "emergency-decision",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);

            var invalidated = await harness.Controller.InvalidatePendingAsync(
                RewardAttributionInvalidationReason.EmergencyStop,
                7.5d,
                1120.5d,
                7.5d,
                CancellationToken.None);

            Assert.That(
                decision.ResultCode,
                Is.EqualTo(PolicyDecisionCycleResultCode.TransitionStarted));
            Assert.That(invalidated, Is.True);
            Assert.That(harness.EnvironmentManager.IsTransitionActive, Is.False);
            Assert.That(harness.Controller.HasPendingOutcome, Is.False);
            Assert.That(
                harness.Controller.Policy.CaptureState().ObservedOutcomeCount,
                Is.Zero);
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.TransitionCancelled));
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.RewardInvalidated));
        }

        [Test]
        public async Task Pause_InvalidatesOpenRewardWithoutPolicyUpdate()
        {
            var harness = CreateHarness(StudyPolicyMode.StaticPersonalized);
            var opportunity = AdvanceToFirstDecision(harness);
            await harness.Controller.ProcessDecisionAsync(
                "pause-decision",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);

            var result = await harness.Controller.TryResolveRewardAsync(
                8d,
                1121d,
                8d,
                VrSessionPhase.Paused,
                true,
                0d,
                0d,
                CancellationToken.None);

            Assert.That(
                result.ResultCode,
                Is.EqualTo(
                    PolicyRewardCycleResultCode.AttributionInvalidated));
            Assert.That(
                harness.Controller.Policy.CaptureState().ObservedOutcomeCount,
                Is.Zero);
            Assert.That(harness.Controller.HasPendingOutcome, Is.False);
        }

        [Test]
        public async Task PolicyUpdateFailure_IsLoggedAndDoesNotBlockSession()
        {
            var rejectingPolicy = new RejectingOutcomePolicy();
            var harness = CreateHarness(
                StudyPolicyMode.StaticPersonalized,
                rejectingPolicy);
            var opportunity = AdvanceToFirstDecision(harness);
            await harness.Controller.ProcessDecisionAsync(
                "rejected-update",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);
            harness.Session.AdvanceTo(8d);
            Assert.That(
                harness.PhysiologyBuffer.Ingest(
                    CreateWindow(1181d, 2.2d),
                    1181d,
                    8d).Accepted,
                Is.True);

            var result = await harness.Controller.TryResolveRewardAsync(
                8d,
                1181d,
                harness.Session.ActiveSessionElapsedSeconds,
                harness.Session.Phase,
                true,
                0d,
                0d,
                CancellationToken.None);

            Assert.That(
                result.ResultCode,
                Is.EqualTo(PolicyRewardCycleResultCode.PolicyUpdateSkipped));
            Assert.That(harness.Controller.HasPendingOutcome, Is.False);
            Assert.That(rejectingPolicy.ObservedOutcomeCount, Is.Zero);
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.BanditUpdateSkipped));
        }

        [Test]
        public async Task NetworkLossDuringTransition_FreezesEnvironmentSafely()
        {
            var harness = CreateHarness(StudyPolicyMode.RuleBasedAdaptive);
            var opportunity = AdvanceToFirstDecision(harness);
            await harness.Controller.ProcessDecisionAsync(
                "network-loss-decision",
                opportunity,
                harness.Session.Phase,
                true,
                PreferredEnvironment(),
                1120d,
                harness.Session.ActiveSessionElapsedSeconds,
                CancellationToken.None);
            harness.Session.AdvanceTo(9d);

            var transition = await harness.Controller.AdvanceTransitionAsync(
                9d,
                1122d,
                harness.Session.ActiveSessionElapsedSeconds,
                harness.Session.Phase,
                false,
                CancellationToken.None);

            Assert.That(
                transition.Status,
                Is.EqualTo(EnvironmentTransitionStatus.Idle));
            Assert.That(harness.Controller.HasPendingOutcome, Is.False);
            Assert.That(harness.EnvironmentManager.CurrentState.Warmth,
                Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(
                harness.Controller.Policy.CaptureState().ObservedOutcomeCount,
                Is.Zero);
            Assert.That(
                harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.RewardInvalidated));
        }

        [Test]
        public async Task StabilizationSelection_LogsCandidatesAndSelectedState()
        {
            var selector = new StabilizationStateSelector(
                new StabilizationSelectionConfiguration(
                    "integration-stabilization",
                    1,
                    3,
                    0.8d,
                    0.2d));
            selector.RecordOutcome(
                new StabilizationOutcome(
                    "safe-outcome",
                    10L,
                    new EnvironmentState(0.6f, 0.5f, 0.5f, 0.5f, 0.5f),
                    1d,
                    false,
                    false));
            var harness = CreateHarness(
                StudyPolicyMode.StaticPersonalized,
                stabilizationSelector: selector);

            var result = await harness.Controller
                .SelectStabilizationStateAsync(
                    VrSessionPhase.Stabilization,
                    PreferredEnvironment(),
                    1200d,
                    12d,
                    CancellationToken.None);

            Assert.That(result.SelectedTransitionId, Is.EqualTo("safe-outcome"));
            Assert.That(harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.StabilizationCandidateScored));
            Assert.That(harness.Sink.EventTypes,
                Does.Contain(TelemetryEventTypes.StabilizationStateSelected));
        }

        private static Harness CreateHarness(
            StudyPolicyMode policyMode,
            IEnvironmentPolicy policyOverride = null,
            SceneEnvironmentProfile sceneProfileOverride = null,
            EnvironmentStateLimits? sessionAdaptationLimits = null,
            StabilizationStateSelector stabilizationSelector = null)
        {
            var ruleConfiguration = new RuleBasedPolicyConfiguration(
                "integration-rules",
                1,
                RuleActivationMode.WorseningStressTrend,
                2d,
                0.1d,
                0.05d);
            var featureVectorBuilder = new PolicyFeatureVectorBuilder();
            var linUcbConfiguration = new LinUcbModelConfiguration(
                "integration-linucb",
                1,
                featureVectorBuilder.FeatureSchemaVersion,
                featureVectorBuilder.FeatureCount,
                1d,
                0.1d);
            var policy = policyOverride;
            if (policy == null)
            {
                Assert.That(
                    StudyPolicyFactory.TryCreate(
                        policyMode,
                        ruleConfiguration,
                        linUcbConfiguration,
                        featureVectorBuilder,
                        out policy,
                        out _),
                    Is.True);
            }

            var physiologyBuffer = new PhysiologyStateBuffer(
                new PhysiologyValidationConfiguration(
                    "integration-physiology",
                    1,
                    20d,
                    1d,
                    0d,
                    0d,
                    0.01d,
                    0.8d,
                    0.8d,
                    8));
            var sceneProfile = sceneProfileOverride ?? CreateSceneProfile();
            var adapter = new RecordingAdapter();
            var environmentManager = new EnvironmentParameterManager(
                sceneProfile.SafeDefault,
                adapter);
            var rewardConfiguration = new RewardPipelineConfiguration(
                "integration-reward",
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
            var sink = new RecordingSink();
            var telemetry = new TelemetryRecorder(
                new TelemetryLoggingConfiguration(
                    "integration-logging",
                    1,
                    "integration-event",
                    "1",
                    4),
                new TelemetrySessionIdentity(
                    "integration-session",
                    "P-TEST"),
                sink,
                () => Guid.NewGuid().ToString("N"));
            var controller = new PolicyController(
                policy,
                new ActionSafetyValidator(),
                environmentManager,
                sceneProfile,
                new ActionSafetyLimits(3, 1d),
                physiologyBuffer,
                rewardConfiguration,
                CreateBaseline(),
                telemetry,
                sessionAdaptationLimits,
                stabilizationSelector);
            var session = new SessionStateMachine();
            var opportunities = new List<SessionDecisionOpportunity>();
            session.DecisionOpportunityReached += opportunities.Add;
            return new Harness(
                controller,
                environmentManager,
                physiologyBuffer,
                session,
                opportunities,
                sink);
        }

        private static SessionDecisionOpportunity AdvanceToFirstDecision(
            Harness harness)
        {
            BootstrapSession(harness.Session);
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
            Assert.That(harness.Session.Phase, Is.EqualTo(VrSessionPhase.Adaptive));
            return harness.Opportunities[0];
        }

        private static void BootstrapSession(SessionStateMachine session)
        {
            Assert.That(session.Initialize(0d), Is.True);
            Assert.That(
                session.AcceptConfiguration(
                    new SessionTimingConfiguration(
                        "integration-session-timing",
                        1,
                        2d,
                        9d,
                        2d,
                        5d),
                    0d),
                Is.True);
            Assert.That(session.MarkSceneLoaded(0d), Is.True);
            Assert.That(
                session.ProcessCommand(
                    "start",
                    SessionCommandType.Start,
                    0d,
                    false).Applied,
                Is.True);
        }

        private static SceneEnvironmentProfile CreateSceneProfile(
            EnvironmentState? safeDefault = null)
        {
            var range = new NormalizedRange(0.2f, 0.8f);
            return new SceneEnvironmentProfile(
                "integration-scene",
                "Integration Scene",
                safeDefault ?? CreateState(0.5f),
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

        private static PhysiologyBaseline CreateBaseline()
        {
            return new PhysiologyBaseline(
                BaselineStandardDeviationMethod.Population,
                new PhysiologyMetricStatistics(3, 2d, 0.5d),
                new PhysiologyMetricStatistics(3, 75d, 2d),
                new PhysiologyMetricStatistics(3, 35d, 2d));
        }

        private static EnvironmentState PreferredEnvironment()
        {
            return new EnvironmentState(0.5f, 0.8f, 0.5f, 0.5f, 0.5f);
        }

        private static EnvironmentState CreateState(float value)
        {
            return new EnvironmentState(value, value, value, value, value);
        }

        private sealed class Harness
        {
            public Harness(
                PolicyController controller,
                EnvironmentParameterManager environmentManager,
                PhysiologyStateBuffer physiologyBuffer,
                SessionStateMachine session,
                List<SessionDecisionOpportunity> opportunities,
                RecordingSink sink)
            {
                Controller = controller;
                EnvironmentManager = environmentManager;
                PhysiologyBuffer = physiologyBuffer;
                Session = session;
                Opportunities = opportunities;
                Sink = sink;
            }

            public PolicyController Controller { get; }

            public EnvironmentParameterManager EnvironmentManager { get; }

            public PhysiologyStateBuffer PhysiologyBuffer { get; }

            public SessionStateMachine Session { get; }

            public List<SessionDecisionOpportunity> Opportunities { get; }

            public RecordingSink Sink { get; }
        }

        private sealed class RecordingAdapter : ISceneEnvironmentAdapter
        {
            public string SceneId => "policy-controller-recording-scene";

            public SceneBindingValidation ValidateBindings()
            {
                return SceneBindingValidation.Succeeded();
            }

            public void ApplyState(EnvironmentState state)
            {
            }
        }

        private sealed class RecordingSink : ITelemetryEventSink
        {
            private readonly List<TelemetryEvent> events =
                new List<TelemetryEvent>();

            public IEnumerable<string> EventTypes =>
                events.Select(telemetryEvent => telemetryEvent.EventType);

            public IEnumerable<TelemetryEvent> Events => events;

            public Task AppendAsync(
                TelemetryEvent telemetryEvent,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(telemetryEvent);
                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }

        private sealed class RejectingOutcomePolicy : IEnvironmentPolicy
        {
            private long decisionCount;

            public string PolicyId => "RejectingOutcomePolicy";

            public string PolicyVersion => "test/1";

            public long ObservedOutcomeCount { get; private set; }

            public PolicyDecision SelectAction(PolicyObservation observation)
            {
                decisionCount++;
                return new PolicyDecision(
                    PolicyId,
                    PolicyVersion,
                    EnvironmentAction.NoChange,
                    observation.Physiology.SequenceNumber,
                    "TEST_REJECT_OUTCOME",
                    null,
                    null,
                    false,
                    null,
                    Array.Empty<PolicyCandidateScore>());
            }

            public void ObserveOutcome(ActionOutcome outcome)
            {
                throw new InvalidOperationException(
                    "Synthetic model-update rejection.");
            }

            public PolicyStateSnapshot CaptureState()
            {
                return new PolicyStateSnapshot(
                    PolicyId,
                    PolicyVersion,
                    "rejecting-policy-state/1",
                    decisionCount,
                    ObservedOutcomeCount,
                    0L);
            }

            public void Reset(PolicyResetContext context)
            {
                decisionCount = 0L;
                ObservedOutcomeCount = 0L;
            }
        }
    }
}
