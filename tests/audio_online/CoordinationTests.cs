using System;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Coordination;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Telemetry;

internal static class CoordinationTests
{
    public static void Run(Action<string, Action> run)
    {
        run("One coordinated owner covers transition and delayed outcome", Exclusive);
        run("Overlapping and already consumed outcomes cannot train both agents", Windows);
        run("Turn handover prefers the peer and tolerates an unavailable agent", Fairness);
        run("Pause invalidates leases and requires fresh post-resume context", Suspension);
        run("Timeout releases a stalled reservation without accepting a late reward", Timeout);
        run("Clip changes wait for learning and cannot repeatedly starve agents", Content);
        run("Concurrent callers cannot acquire two reservations", ConcurrentAcquisition);
        run("Visual policy reserves its turn and rejects cancelled reward updates", VisualPolicy);
        run("Visual exception releases its reservation", VisualException);
        run("Late visual completion cannot train or release a newer audio turn", LateVisualCompletion);
        run("Responsive visual bandit admits lower quality and rewards small stress changes", ResponsiveVisual);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static void ResponsiveVisual()
    {
        var production = new VisualHarness(new AdaptationCoordinator());
        production.AddWindow(100, 100, 0.4);
        Check(production.Decide(100).ResultCode == PolicyDecisionCycleResultCode.SkippedPhysiologyUnavailable,
            "Production visual quality gate was weakened");
        var coordination = new AdaptationCoordinator();
        var testing = new VisualHarness(coordination, responsiveTesting: true);
        testing.AddWindow(100, 100, 0.4);
        Check(testing.Decide(100).ResultCode == PolicyDecisionCycleResultCode.RewardWindowOpened,
            "Testing visual input was rejected");
        testing.AddWindow(160, 160, 0.4, 1.97);
        Check(testing.Resolve(160).ResultCode == PolicyRewardCycleResultCode.Waiting,
            "Testing visual reward accepted a settling-overlapping window");
        testing.AddWindow(170, 170, 0.4, 1.97);
        Check(testing.Resolve(170).ResultCode == PolicyRewardCycleResultCode.RewardApplied
            && testing.Policy.CaptureState().ModelUpdateCount == 1 && coordination.Status.CompletedTurns == 1,
            "Testing visual bandit failed to learn from a normalized 0.01 stress change");
        var next = testing.Decide(176);
        Check(next.ResultCode == PolicyDecisionCycleResultCode.TransitionStarted
            && next.Validation.HasValue && next.Validation.Value.ExecutedAction != EnvironmentAction.NoChange,
            "Real visual exploration did not select a safe control change after learning");
    }
    private static long Acquire(AdaptationCoordinator c, AdaptationOwner owner, double end = 100, double now = 100)
    {
        Check(c.TryAcquire(owner, owner.ToString(), end - 60, end, now, now, 240, out var lease), "Reservation denied");
        return lease;
    }

    private static void Exclusive()
    {
        var c = new AdaptationCoordinator(); var visual = Acquire(c, AdaptationOwner.Visual);
        Check(!c.TryAcquire(AdaptationOwner.Audio, "audio", 40, 100, 101, 101, 240, out _), "Two owners");
        Check(!c.TryBeginContent("clip", 101, 60, out _), "Clip swap during measurement");
        Check(!c.TryAcceptOutcome(visual, 101, 161), "Unsettled transition accepted");
        c.MarkTransitionCompleted(visual, 110, 5);
        Check(!c.TryAcceptOutcome(visual, 114, 174), "Settling overlap accepted");
        Check(c.TryAcceptOutcome(visual, 115, 175), "Clean outcome rejected");
        Check(c.Status.Owner == AdaptationOwner.Visual, "Owner released before model update finished");
        Check(!c.TryAcceptOutcome(visual, 115, 175), "Duplicate outcome accepted");
        c.Release(visual, 175, 175, "Visual finished");
        Check(c.Status.CompletedTurns == 1 && c.Status.Owner == AdaptationOwner.None, "Turn did not finish");
    }

    private static void Windows()
    {
        var c = new AdaptationCoordinator(); var v = Acquire(c, AdaptationOwner.Visual);
        c.MarkTransitionCompleted(v, 100, 5); c.TryAcceptOutcome(v, 110, 170); c.Release(v, 170, 170, "done");
        Check(!c.TryAcquire(AdaptationOwner.Audio, "a", 40, 100, 170, 170, 240, out _), "Stale pre-window accepted");
        var a = Acquire(c, AdaptationOwner.Audio, 170, 170);
        c.MarkTransitionCompleted(a, 170, 5);
        Check(!c.TryAcceptOutcome(a, 110, 170), "Visual outcome reused by audio");
        Check(c.TryAcceptOutcome(a, 175, 235), "New audio outcome rejected");
    }

    private static void Fairness()
    {
        var c = new AdaptationCoordinator(); var v = Acquire(c, AdaptationOwner.Visual);
        c.MarkTransitionCompleted(v, 100, 5); c.TryAcceptOutcome(v, 110, 170); c.Release(v, 170, 170, "done");
        Check(!c.CanRequest(AdaptationOwner.Visual, 110, 170, 171, 171), "Same owner bypassed peer");
        Check(c.CanRequest(AdaptationOwner.Audio, 110, 170, 171, 171), "Peer cannot take next turn");
        Check(c.CanRequest(AdaptationOwner.Visual, 115, 175, 176, 176), "Missing peer blocks indefinitely");
    }

    private static void Suspension()
    {
        var c = new AdaptationCoordinator(); var old = Acquire(c, AdaptationOwner.Audio);
        c.SetSuspended(true, 110, "Pause");
        Check(!c.IsCurrent(old) && !c.TryAcceptOutcome(old, 120, 180), "Paused outcome accepted");
        Check(!c.TryBeginContent("clip", 120, 60, out _), "Content changed during suspension");
        c.SetSuspended(false, 130, "Resume");
        Check(!c.CanRequest(AdaptationOwner.Visual, 100, 160, 160, 160), "Pre-resume context reused");
        var current = Acquire(c, AdaptationOwner.Visual, 200, 200);
        c.Release(old, 201, 201, "Late callback");
        c.CancelReservation(old, 201, "Late failure");
        Check(c.IsCurrent(current), "Old callback released newer turn");
    }

    private static void Timeout()
    {
        var c = new AdaptationCoordinator(); var old = Acquire(c, AdaptationOwner.Audio);
        c.Tick(341, 341);
        Check(!c.IsCurrent(old) && c.Status.CancelledTurns == 1, "Timeout kept the lock");
        Check(!c.TryAcceptOutcome(old, 350, 410), "Late reward updated after timeout");
        Acquire(c, AdaptationOwner.Visual, 410, 410);
    }

    private static void Content()
    {
        var c = new AdaptationCoordinator();
        Check(c.TryBeginContent("clip", 100, 60, out var content), "Content reservation denied");
        Check(!c.CanRequest(AdaptationOwner.Audio, 40, 100, 100, 100), "Learning during content swap");
        c.Release(content, 110, 110, "Clip ready");
        Check(!c.TryBeginContent("next clip", 111, 60, out _), "Regeneration starves learning");
        Check(!c.CanRequest(AdaptationOwner.Audio, 100, 160, 160, 160), "Context contains clip transition");
        var a = Acquire(c, AdaptationOwner.Audio, 180, 180);
        c.MarkTransitionCompleted(a, 180, 5); c.TryAcceptOutcome(a, 185, 245); c.Release(a, 245, 245, "done");
        Check(c.TryBeginContent("next clip", 251, 60, out _), "Content remains blocked after clean learning turn");
    }

    private static void ConcurrentAcquisition()
    {
        var c = new AdaptationCoordinator(); int acquired = 0;
        Parallel.For(0, 20, i =>
        {
            if (c.TryAcquire(i % 2 == 0 ? AdaptationOwner.Audio : AdaptationOwner.Visual,
                "decision" + i, 40, 100, 100, 100, 240, out _)) Interlocked.Increment(ref acquired);
        });
        Check(acquired == 1, "Concurrent double owner");
    }

    private static void VisualPolicy()
    {
        var c = new AdaptationCoordinator(); var harness = new VisualHarness(c);
        var audio = Acquire(c, AdaptationOwner.Audio);
        harness.AddWindow(100, 100);
        var blocked = harness.Decide(100);
        Check(blocked.ResultCode == PolicyDecisionCycleResultCode.SkippedCoordination
            && harness.Policy.CaptureState().DecisionCount == 0, "Blocked visual selected an action");
        c.Invalidate(100, "Audio cancelled");
        harness.AddWindow(170, 170);
        Check(harness.Decide(170).ResultCode == PolicyDecisionCycleResultCode.RewardWindowOpened, "Visual did not acquire");
        Check(c.Status.Owner == AdaptationOwner.Visual, "Visual does not own reward window");
        c.Invalidate(175, "Unexpected sound change");
        harness.AddWindow(240, 240);
        var cancelled = harness.Resolve(240);
        Check(cancelled.ResultCode == PolicyRewardCycleResultCode.AttributionInvalidated
            && harness.Policy.CaptureState().ModelUpdateCount == 0, "Cancelled visual reward trained");
        harness.AddWindow(310, 310);
        Check(harness.Decide(310).ResultCode == PolicyDecisionCycleResultCode.RewardWindowOpened, "Visual cannot recover");
        harness.AddWindow(380, 380);
        Check(harness.Resolve(380).ResultCode == PolicyRewardCycleResultCode.RewardApplied, "Valid visual outcome rejected");
        Check(harness.Policy.CaptureState().ModelUpdateCount == 1 && c.Status.CompletedTurns == 1,
            "Valid visual outcome did not train once");
    }

    private static void VisualException()
    {
        var c = new AdaptationCoordinator(); var harness = new VisualHarness(c);
        harness.AddWindow(100, 100); harness.Sink.FailNextActionProposal = true;
        bool failed = false;
        try { harness.Decide(100); } catch (InvalidOperationException) { failed = true; }
        Check(failed && c.Status.Owner == AdaptationOwner.None && !harness.Controller.HasPendingOutcome,
            "Failed visual operation leaked reservation");
    }

    private static void LateVisualCompletion()
    {
        var c = new AdaptationCoordinator(); var h = new VisualHarness(c);
        h.AddWindow(100, 100); h.Decide(100);
        h.AddWindow(170, 170); h.Sink.PauseRewardCalculation = true;
        var pending = h.Controller.TryResolveRewardAsync(170, 170, 170, VrSessionPhase.Adaptive,
            true, CancellationToken.None);
        Check(!pending.IsCompleted, "Test did not pause asynchronous reward logging");
        c.Invalidate(175, "Interrupted output");
        var audio = Acquire(c, AdaptationOwner.Audio, 240, 240);
        h.Sink.ContinueReward.SetResult(true);
        Check(pending.GetAwaiter().GetResult().ResultCode == PolicyRewardCycleResultCode.AttributionInvalidated,
            "Late completion accepted a cancelled reward");
        Check(h.Policy.CaptureState().ModelUpdateCount == 0 && c.IsCurrent(audio),
            "Old visual completion changed weights or released the new audio turn");
    }

    private sealed class VisualHarness
    {
        public readonly IEnvironmentPolicy Policy;
        public readonly PolicyController Controller;
        public readonly RecordingSink Sink = new RecordingSink();
        private readonly PhysiologyStateBuffer buffer;
        private int sequence;

        public VisualHarness(AdaptationCoordinator coordination, bool responsiveTesting = false)
        {
            var features = new PolicyFeatureVectorBuilder();
            var model = new LinUcbModelConfiguration("bandit", 1, features.FeatureSchemaVersion, features.FeatureCount, 1, .1);
            if (responsiveTesting) model = ResponsiveTestingPreset.Apply(model);
            Check(StudyPolicyFactory.TryCreate(StudyPolicyMode.ContextualBandit,
                new RuleBasedPolicyConfiguration("rules", 1, RuleActivationMode.WorseningStressTrend, 2, .1, .05),
                model,
                features, out Policy, out _), "Cannot create visual bandit");
            var range = new NormalizedRange(.2f, .8f);
            var state = new EnvironmentState(.5f, .5f, .5f, .5f, .5f);
            var scene = new SceneEnvironmentProfile("test", "Test", state,
                new EnvironmentStateLimits(range, range, range, range, range),
                new EnvironmentActionStepConfiguration(.1f, .1f, .1f, .1f, .1f), 2, 0);
            var validation = new PhysiologyValidationConfiguration("physiology", 1,
                90, 1, 0, 0, .01, .8, .8, 8);
            buffer = new PhysiologyStateBuffer(responsiveTesting ? ResponsiveTestingPreset.Apply(validation) : validation);
            var reward = new RewardPipelineConfiguration("reward", 1, BaselineStandardDeviationMethod.Population,
                3, .01, 3, 3, 5, 240, 1, 0, 0, .1, 0, 1);
            Controller = new PolicyController(Policy, new ActionSafetyValidator(),
                new EnvironmentParameterManager(state, new Adapter()), scene, new ActionSafetyLimits(3, 1),
                buffer, reward, new PhysiologyBaseline(BaselineStandardDeviationMethod.Population,
                    new PhysiologyMetricStatistics(3, 2, .5), new PhysiologyMetricStatistics(3, 75, 2),
                    new PhysiologyMetricStatistics(3, 35, 2)),
                new TelemetryRecorder(new TelemetryLoggingConfiguration("log", 1, "event", "1", 4),
                    new TelemetrySessionIdentity("session", "participant"), Sink, () => Guid.NewGuid().ToString("N")))
                { Coordination = coordination };
        }

        public void AddWindow(double end, double mono, double quality = .95, double stress = 2)
        {
            var window = new PhysiologyWindow(end, end - 60, end, 75, 35, 40,
                new StressDecision(StressDecisionMode.Point, 2, null, null, "moderate", .9, false,
                    new StressProbabilityVector(.1, .2, .6, .1), stress), quality);
            Check(buffer.Ingest(window, end, mono).Accepted, "Physiology rejected");
        }
        public PolicyDecisionCycleResult Decide(double now) => Controller.ProcessDecisionAsync("visual-" + (++sequence),
            new SessionDecisionOpportunity(sequence, now, now), VrSessionPhase.Adaptive, true,
            new EnvironmentState(.5f, .5f, .5f, .5f, .5f), now, now, CancellationToken.None).GetAwaiter().GetResult();
        public PolicyRewardCycleResult Resolve(double now) => Controller.TryResolveRewardAsync(now, now, now,
            VrSessionPhase.Adaptive, true, CancellationToken.None).GetAwaiter().GetResult();
    }

    private sealed class Adapter : ISceneEnvironmentAdapter
    {
        public string SceneId => "test";
        public SceneBindingValidation ValidateBindings() => SceneBindingValidation.Succeeded();
        public void ApplyState(EnvironmentState state) { }
    }

    private sealed class RecordingSink : ITelemetryEventSink
    {
        public bool FailNextActionProposal;
        public bool PauseRewardCalculation;
        public readonly TaskCompletionSource<bool> ContinueReward = new TaskCompletionSource<bool>();
        public Task AppendAsync(TelemetryEvent value, CancellationToken token)
        {
            if (FailNextActionProposal && value.EventType == TelemetryEventTypes.ActionProposed)
            { FailNextActionProposal = false; throw new InvalidOperationException("Simulated telemetry failure"); }
            if (PauseRewardCalculation && value.EventType == TelemetryEventTypes.RewardCalculated)
                return ContinueReward.Task;
            return Task.CompletedTask;
        }
        public Task FlushAsync(CancellationToken token) => Task.CompletedTask;
    }
}
