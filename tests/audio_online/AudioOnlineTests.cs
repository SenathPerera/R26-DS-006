using System;
using System.IO;
using System.Reflection;
using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.RL.Agent;
using AdaptiveAudioVR.Preference;
using LaminarVR.AdaptiveMeditation.Coordination;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Policy;
using UnityEngine;

internal static class AudioOnlineTests
{
    private static int passed;
    private static readonly double Epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;

    private static int Main(string[] args)
    {
        Application.persistentDataPath = args[0];
        Directory.CreateDirectory(args[0]);
        try
        {
            Run("LinUCB known ridge regression result", KnownMath);
            Run("LinUCB eligible-action masking and deterministic tie", Masking);
            Run("Audio exploration rotates tied safe actions without inventing rewards", ExplorationTies);
            Run("Discarded audio outcomes report bounded diagnostic categories", OutcomeReasons);
            Run("Invalid observations leave model unchanged", InvalidMath);
            Run("Snapshot round trip and atomic corrupt-snapshot rejection", Snapshots);
            Run("User, environment, configuration and simulation model isolation", Persistence);
            Run("Relay audio preferences bind session and participant", RelayAudioPreferences);
            Run("40-feature context contract and missing physiology flags", Features);
            Run("All 13 actions map safely to seven control dimensions", Actions);
            Run("Safety NoChange cannot repeat a previous action", AbsoluteHold);
            Run("Safety rejects NaN and freezes at low confidence", InvalidSafety);
            Run("Outcome rejects overlap, duplicates and transition windows", Attribution);
            Run("Outcome validation rejects stale/future/low-quality input", WindowValidation);
            Run("Runtime learns once, only after settled post-window", DelayedOnlineUpdate);
            Run("Static and rule conditions do not learn or add residuals", BaselineConditions);
            Run("Emergency, pause and contaminated outcomes never learn", InvalidRuntimeOutcomes);
            Run("Actual playback must reach target before attribution", PlaybackAttribution);
            Run("Simulation updates cannot enter physiology model", DomainSwitch);
            Run("Missing RMSSD cannot create a fabricated HRV reward", MissingPhysiologyReward);
            Run("Overlapping baseline windows do not count as independent samples", BaselineOverlap);
            Run("Frozen online evaluation computes rewards without updating weights", FrozenEvaluation);
            Run("Replay snapshots cannot mutate stored outcome evidence", ReplayIsolation);
            Run("Monitoring keeps decision and delayed outcome attribution separate", MonitorAttribution);
            Run("Low-quality and expired pending outcomes never update weights", InvalidAndExpired);
            Run("Synthetic contextual learning improves choices", ContextLearning);
            CoordinationTests.Run(Run);
            Run("Audio holding new decisions preserves its pending outcome", HoldDecisionAdmission);
            Run("Audio waits for visual reward then learns only its own outcome", CoordinatedAudio);
            Run("Audio retries a blocked turn using the same still-fresh publication", RetryCoordinatedAudio);
            Run("Coordinated audio models do not load independent-controller weights", CoordinatedModelIdentity);
            Run("Responsive test preset preserves window validation and action limits", ResponsivePreset);
            Run("Responsive audio admits lower reliability without changing production gates", ResponsiveAudio);
            Run("Responsive audio still waits for its coordinated clean reward window", ResponsiveAttribution);
            GenerationPlaybackTests.Run(Run);
            Console.WriteLine("PASS: " + passed + " audio online-learning tests. Runtime tests use explicit Unity test doubles.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < 1e-6, actual + " != " + expected);
    private static void Throws(Action action) { bool threw = false; try { action(); } catch (ArgumentException) { threw = true; } Check(threw, "Invalid input accepted"); }

    private static void KnownMath()
    {
        var model = new AudioLinUcbModel(2, 3, 0.25, 1);
        model.Observe(1, new[] { 1d, 0d }, 1);
        var result = model.Select(new[] { 1d, 0d }, new[] { 1 });
        Near(result.Mean, 0.5); Near(result.Uncertainty, Math.Sqrt(0.5));
        Near(result.Score, 0.5 + 0.25 * Math.Sqrt(0.5));
        Near(model.Capture().arms[0].covariance[0], 1);
    }

    private static void Masking()
    {
        var model = new AudioLinUcbModel(1, 3, 0.25, 1);
        Check(model.Select(new[] { 1d }, new[] { 2, 0, 1 }).Arm == 0, "Tie must prefer NoChange");
        model.Observe(2, new[] { 1d }, 3);
        Check(model.Select(new[] { 1d }, new[] { 0, 1 }).Arm != 2, "Masked action selected");
    }

    private static void InvalidMath()
    {
        var model = new AudioLinUcbModel(2, 2, 0.25, 1);
        Throws(() => model.Observe(0, new[] { double.NaN, 0 }, 1));
        Throws(() => model.Observe(0, new[] { 1d }, 1));
        Throws(() => model.Observe(0, new[] { 1d, 0 }, double.PositiveInfinity));
        Check(model.UpdateCount == 0, "Invalid sample updated model");
    }

    private static void ExplorationTies()
    {
        var policy = new AudioOnlinePolicy(0.03f, 0.25, 1);
        var features = new double[AudioBanditFeatures.Count]; features[0] = 1;
        var eligible = new[] { 5, 0, 2 };
        Check(policy.Select(AudioRLPolicyMode.PersonalizedBandit, null, features, eligible).Arm == 0, "First tied action must hold");
        Check(policy.Select(AudioRLPolicyMode.PersonalizedBandit, null, features, eligible).Arm == 2, "Repeated hold starved equally scored safe action");
        Check(policy.Select(AudioRLPolicyMode.PersonalizedBandit, null, features, eligible).Arm == 5, "Tie exploration ignored eligible action");
        Check(policy.Model.UpdateCount == 0, "Exploration attempt fabricated an observation");
        for (int i = 0; i < 3; i++)
            Check(policy.Select(AudioRLPolicyMode.PersonalizedBandit, null, features, eligible, false).Arm == 0, "Frozen evaluation explored");
        policy.Model.Observe(5, features, 3);
        Check(policy.Select(AudioRLPolicyMode.PersonalizedBandit, null, features, eligible).Arm == 5, "Tie balancing replaced a strictly better score");
        Check(policy.Select(AudioRLPolicyMode.PersonalizedBandit, null, features, new[] { 0 }).Arm == 0, "Masked action escaped safety gate");
    }

    private static void OutcomeReasons()
    {
        Check(AudioRLTransition.DescribeInvalidOutcome("Invalid or unreliable stress/confidence/quality.") == "Discarded_InputReliability", "Input gate hidden");
        Check(AudioRLTransition.DescribeInvalidOutcome("Realtime playback gap or dropped PCM; outcome discarded") == "Discarded_PlaybackInterrupted", "Playback cause hidden");
        Check(AudioRLTransition.DescribeInvalidOutcome("Visual environment changed during audio attribution") == "Discarded_VisualChange", "Visual interference hidden");
        Check(AudioRLTransition.DescribeInvalidOutcome("No eligible outcome arrived before timeout") == "Discarded_Timeout", "Timeout hidden");
        Check(AudioRLTransition.DescribeInvalidOutcome("private-user https://secret.invalid/?key=secret") == "Discarded", "Unrecognized details leaked to monitor");
    }

    private static void Snapshots()
    {
        var model = new AudioLinUcbModel(2, 2, 0.25, 1);
        model.Observe(1, new[] { 1d, -0.5 }, 0.8);
        var restored = new AudioLinUcbModel(2, 2, 0.25, 1);
        Check(restored.TryRestore(model.Capture(), out _), "Roundtrip failed");
        Near(model.Select(new[] { 1d, 0.2 }, new[] { 1 }).Score, restored.Select(new[] { 1d, 0.2 }, new[] { 1 }).Score);
        var bad = model.Capture(); bad.arms[0].covariance[0] = -1;
        Check(!restored.TryRestore(bad, out _), "Negative definite matrix restored");
        Check(restored.UpdateCount == 1, "Failed restore mutated model");
        bad = model.Capture(); bad.arms[1].rewardVector[0] = double.NaN;
        Check(!restored.TryRestore(bad, out _), "NaN snapshot accepted");
    }

    private static void Persistence()
    {
        var a = new AudioBanditPersistence(Application.persistentDataPath, "user-a", "temple", false, "reward1");
        Check(a.FilePath != new AudioBanditPersistence(Application.persistentDataPath, "user-b", "temple", false, "reward1").FilePath, "Users share model");
        Check(a.FilePath != new AudioBanditPersistence(Application.persistentDataPath, "user-a", "temple", true, "reward1").FilePath, "Simulation shares model");
        Check(a.FilePath != new AudioBanditPersistence(Application.persistentDataPath, "user-a", "ocean", false, "reward1").FilePath, "Environments share model");
        Check(a.FilePath != new AudioBanditPersistence(Application.persistentDataPath, "user-a", "temple", false, "reward2").FilePath, "Reward versions share model");
        var model = new AudioLinUcbModel(AudioBanditFeatures.Count, AudioActionCatalog.Count, 0.25, 1);
        var context = new double[AudioBanditFeatures.Count]; context[0] = 1;
        model.Observe(1, context, 1);
        Check(a.TrySave(model, out string error), error);
        Check(a.TrySave(model, out error), "Atomic replacement failed: " + error);
        var restored = new AudioLinUcbModel(AudioBanditFeatures.Count, AudioActionCatalog.Count, 0.25, 1);
        Check(a.TryLoad(restored, out error) && restored.UpdateCount == 1, error);
    }

    private static void RelayAudioPreferences()
    {
        const string profile = "{\"preferredInstruments\":[\"piano\",\"flute\"],\"mood\":\"calm\",\"tempoPreference\":\"slow\",\"audioIntensity\":0.3,\"ambientMusicBalance\":\"balanced\",\"brightnessPreference\":\"soft_dark\",\"noveltyTolerance\":0.2,\"avoidDissonance\":true,\"rhythmPreference\":\"minimal\",\"natureSoundPreference\":\"medium\",\"reverbPreference\":\"spacious\",\"volumePreference\":\"low\"}";
        const string overrideProfile = "{\"preferredInstruments\":[\"bells\"],\"mood\":\"sleepy\",\"tempoPreference\":\"slow\",\"audioIntensity\":0.1,\"ambientMusicBalance\":\"mostly_ambience\",\"brightnessPreference\":\"soft_dark\",\"noveltyTolerance\":0.1,\"avoidDissonance\":true,\"rhythmPreference\":\"minimal\",\"natureSoundPreference\":\"low\",\"reverbPreference\":\"spacious\",\"volumePreference\":\"low\"}";
        string json = "{\"payload\":{\"sessionId\":\"session-7\",\"participantPseudonym\":\"participant-7\",\"sceneId\":\"temple-pond\",\"audioPreferences\":{\"schemaVersion\":\"mindsync-audio-preferences-v1\",\"longTerm\":" + profile + ",\"preferenceMode\":\"adjust\",\"sessionOverride\":" + overrideProfile + "}}}";
        var configuration = RelayAudioConfiguration("message-1", "participant-7", json);
        Check(AudioSessionPreferenceMapper.TryMap(configuration, out var mapped, out var error), error);
        Check(mapped.LongTerm.userId == "participant-7" && mapped.Effective.userId == "participant-7", "Wrong participant profile");
        Check(mapped.LongTerm.preferredInstruments[0] == "piano" && mapped.Effective.preferredInstruments[0] == "bells", "Session adjustment changed long-term sound");
        Check(mapped.Effective.preferredAmbience == "temple", "Scene did not set musical setting");
        string usualJson = json.Replace("\"preferenceMode\":\"adjust\",\"sessionOverride\":" + overrideProfile,
            "\"preferenceMode\":\"usual\",\"sessionOverride\":null");
        var usualConfiguration = RelayAudioConfiguration("message-usual", "participant-7", usualJson);
        Check(AudioSessionPreferenceMapper.TryMap(usualConfiguration, out var usual, out error), error);
        Check(usual.Effective.preferredInstruments[0] == "piano", "Usual mode ignored long-term sound");
        var wrongParticipant = RelayAudioConfiguration("message-2", "participant-8", json);
        Check(!AudioSessionPreferenceMapper.TryMap(wrongParticipant, out _, out _), "Cross participant payload accepted");
        var missingAudio = RelayAudioConfiguration("message-3", "participant-7", "{\"payload\":{\"sessionId\":\"session-7\"}}");
        Check(!AudioSessionPreferenceMapper.TryMap(missingAudio, out _, out _), "Missing preferences accepted");
    }

    private static SessionRelayConfigurationMessage RelayAudioConfiguration(string messageId, string participantId, string rawJson)
    {
        var context = new SessionContextSnapshot("mindsync-session-context-v1", "2026-10-10T08:30:00.000Z", 6, -0.2, 0.7, 0.4, false, 510, 2, 3);
        return new SessionRelayConfigurationMessage("mindsync-session-v3", messageId, "session-7", participantId, "temple-pond",
            StudyPolicyMode.ContextualBandit, new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f), context, rawJson);
    }

    private static AudioParameters Parameters() => AudioParameters.FromControlVector(new[] { 0.4f, 0.4f, 0.4f, 0.3f, 0.7f, 0.45f, 0.55f });
    private static AudioRLState State() => new AudioRLState { currentParameters = Parameters(), personalizedBaseline = Parameters(), signal = new SignalPacket(0.6f, 0.9f, 0), preferenceEncoding = new float[13] };

    private static void Features()
    {
        var state = State(); var encoded = AudioBanditFeatures.Encode(state, 0.08f);
        Check(encoded.Length == 40, "Feature dimension changed");
        Near(encoded[35], 0); Near(encoded[36], 0);
        state.signal.hasHeartRate = state.signal.hasRmssd = true;
        state.signal.heartRate = 80; state.signal.rmssd = 30;
        encoded = AudioBanditFeatures.Encode(state, 0.08f);
        Near(encoded[35], 1); Near(encoded[36], 1);
    }

    private static void Actions()
    {
        for (int arm = 0; arm < AudioActionCatalog.Count; arm++)
        {
            var action = AudioActionCatalog.Action(arm, 0.03f);
            Check(AudioActionCatalog.Match(action, 0.03f) == arm, "Arm roundtrip mismatch");
            Near(action.ApplyTo(Parameters()).musicMix + action.ApplyTo(Parameters()).ambientMix, 1);
            Check(action.MaximumAbsoluteMagnitude <= 0.03001f, "Action too large");
        }
    }

    private static AudioRLSafetyFilter Filter() => new AudioRLSafetyFilter(0.08f, 0.25f, 0.45f, 0.30f, 0.60f, 0.3f, 0.05f, 0.15f, 0.85f);

    private static void AbsoluteHold()
    {
        var state = State();
        state.currentParameters.intensity = 0.99f;
        var held = Filter().Apply(AudioRLAction.NoChange, AudioActionCatalog.Action(1, 0.08f), state, true, true);
        Near(held.finalSafeAction.MaximumAbsoluteMagnitude, 0);
        Near(held.safeTarget.intensity, 0.99f);
    }

    private static void InvalidSafety()
    {
        var state = State(); state.signal.confidence = 0.2f;
        Near(Filter().Apply(AudioActionCatalog.Action(1, 0.03f), AudioRLAction.NoChange, state, true, true).finalSafeAction.MaximumAbsoluteMagnitude, 0);
        state.signal.confidence = 0.9f;
        Near(Filter().Apply(new AudioRLAction { deltaIntensity = float.NaN }, AudioRLAction.NoChange, state, true, true).finalSafeAction.MaximumAbsoluteMagnitude, 0);
    }

    private static SignalPacket Window(double end, long sequence = 1, float stress = 0.7f) => new SignalPacket(stress, 0.9f, 0.95f, 78, 35, 40,
        Epoch + end, Epoch + end - 60, Epoch + end, sequence, (float)end);

    private static void Attribution()
    {
        var before = Window(120, 2);
        Check(!AudioOutcomeGate.CanAttribute(before, Window(180, 3), Epoch + 126, 0, out _), "Transition-contaminated window accepted");
        Check(AudioOutcomeGate.CanAttribute(before, Window(240, 4), Epoch + 126, 0, out _), "Clean post window rejected");
        Check(!AudioOutcomeGate.CanAttribute(before, Window(240, 4), Epoch + 126, Epoch + 240, out _), "Duplicate outcome accepted");
    }

    private static void WindowValidation()
    {
        var signal = Window(120, 2);
        Check(AudioOutcomeGate.IsValidWindow(signal, Epoch + 120, 0.6f, 0.45f, out _), "Valid window rejected");
        Check(!AudioOutcomeGate.IsValidWindow(signal, Epoch + 250, 0.6f, 0.45f, out _), "Stale window accepted");
        Check(!AudioOutcomeGate.IsValidWindow(signal, Epoch + 100, 0.6f, 0.45f, out _), "Future window accepted");
        signal.signalQuality = 0.1f;
        Check(!AudioOutcomeGate.IsValidWindow(signal, Epoch + 120, 0.6f, 0.45f, out _), "Poor quality accepted");
    }

    private static AudioRLAgent Agent(AudioRLPolicyMode mode = AudioRLPolicyMode.PersonalizedBandit, bool responsiveTesting = false)
    {
        var agent = new AudioRLAgent();
        agent.ConfigureResponsiveTesting(responsiveTesting);
        typeof(AudioRLAgent).GetField("persistPersonalizedModel", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(agent, false);
        agent.SetPolicyMode(mode);
        var p = Parameters();
        agent.Initialize(new AudioProfile { userId = "test", baseIntensity = p.intensity, baseDensity = p.density,
            baseBrightness = p.brightness, baseTempo = p.tempo, baseFade = p.fade, baseMusicMix = p.musicMix, baseAmbientMix = p.ambientMix }, PersonalizationStrategy.CreateNeutral());
        return agent;
    }

    private static void Tick(AudioRLAgent agent, double time, SignalPacket signal, bool safe = true)
    {
        Time.time = (float)time; Time.realtimeSinceStartupAsDouble = time;
        agent.EvaluateAt(signal, 1, safe, time, Epoch + time);
    }

    private static AudioRLAgent Warmup(AudioRLPolicyMode mode = AudioRLPolicyMode.PersonalizedBandit)
    {
        var agent = Agent(mode);
        Tick(agent, 60, Window(60)); Tick(agent, 120, Window(120, 2));
        Check(agent.DecisionCount == 1, "No decision after two unique baseline windows");
        return agent;
    }

    private static void HoldDecisionAdmission()
    {
        var agent = Warmup();
        agent.SetControlContext(false, true, "No new decisions");
        Tick(agent, 121, Window(120, 2)); Tick(agent, 240, Window(240, 4));
        Check(agent.OnlineUpdateCount == 1 && agent.DecisionCount == 1,
            "Blocking new decisions cancelled the pending outcome");
    }

    private static void CoordinatedAudio()
    {
        var c = new AdaptationCoordinator(); var agent = Agent(); agent.BindCoordination(c);
        Check(c.TryAcquire(AdaptationOwner.Visual, "visual", Epoch, Epoch + 60, 60, Epoch + 60, 240, out var visual), "Visual reservation");
        c.MarkTransitionCompleted(visual, Epoch + 120, 5);
        Tick(agent, 60, Window(60)); Tick(agent, 120, Window(120, 2));
        Check(agent.DecisionCount == 0 && !agent.HasPendingOutcome, "Audio acted during visual turn");
        Check(c.TryAcceptOutcome(visual, Epoch + 180, Epoch + 240), "Visual outcome");
        c.Release(visual, 240, Epoch + 240, "Visual completed");
        Tick(agent, 240, Window(240, 4));
        Check(agent.DecisionCount == 1 && c.Status.Owner == AdaptationOwner.Audio, "Audio handover failed");
        Tick(agent, 241, Window(240, 4)); Tick(agent, 300, Window(300, 5));
        Check(agent.OnlineUpdateCount == 0, "Audio reused contaminated window");
        Tick(agent, 360, Window(360, 6));
        Check(agent.OnlineUpdateCount == 1 && c.Status.CompletedTurns == 2, "Audio did not learn own outcome");
        Tick(agent, 361, Window(360, 6));
        Check(agent.OnlineUpdateCount == 1, "Outcome reused");
        c.SetSuspended(true, Epoch + 362, "Pause");
        Tick(agent, 363, Window(360, 6));
        Check(agent.OnlineUpdateCount == 1 && !agent.HasPendingOutcome, "Paused reservation trained");
    }

    private static void RetryCoordinatedAudio()
    {
        var c = new AdaptationCoordinator(); var a = Agent(); a.BindCoordination(c);
        Check(c.TryAcquire(AdaptationOwner.Visual, "v", Epoch, Epoch + 60, 60, Epoch + 60, 240, out var lease), "Visual turn");
        c.MarkTransitionCompleted(lease, Epoch + 61, 5);
        Tick(a, 60, Window(60)); Tick(a, 120, Window(120, 2)); Tick(a, 180, Window(180, 3));
        Check(a.DecisionCount == 0, "Audio bypassed visual owner");
        Check(c.TryAcceptOutcome(lease, Epoch + 120, Epoch + 180), "Visual outcome");
        c.Release(lease, 180, Epoch + 180, "done");
        Tick(a, 181, Window(180, 3));
        Check(a.DecisionCount == 1, "Audio waited for another sensor publication after handover");
    }

    private static void CoordinatedModelIdentity()
    {
        var a = Agent(); Tick(a, 60, Window(60)); string independent = a.ModelPath;
        a.BindCoordination(new AdaptationCoordinator()); Tick(a, 61, Window(60));
        Check(a.ModelPath != independent && a.OnlineUpdateCount == 0, "Old reward regime reused its model");
    }

    private static void DelayedOnlineUpdate()
    {
        var agent = Warmup();
        Tick(agent, 121, Window(120, 2));
        Tick(agent, 180, Window(180, 3, 0.6f));
        Check(agent.OnlineUpdateCount == 0, "Learned from early post window");
        Tick(agent, 240, Window(240, 4, 0.5f));
        Check(agent.OnlineUpdateCount == 1 && agent.RewardVersion == 1, "Valid outcome did not update once");
        Tick(agent, 241, Window(240, 4, 0.5f));
        Check(agent.OnlineUpdateCount == 1, "Reused physiology updated twice");
        Check(agent.GetReplaySnapshot()[0].onlineUpdateApplied, "Update missing from audit log");
    }

    private static void MonitorAttribution()
    {
        var agent = Warmup();
        Check(agent.MonitorDecision != null && agent.MonitorOutcome == null, "Unobserved reward was reported");
        Near(agent.MonitorDecision.state.signal.stress, Window(120, 2).stress);
        Tick(agent, 121, Window(120, 2));
        Tick(agent, 180, Window(180, 3, 0.6f));
        Tick(agent, 240, Window(240, 4, 0.5f));
        Check(agent.MonitorOutcome.rewardValid && agent.MonitorOutcomeDecision == 1,
            "Reward attributed to a newer decision");
        var outcome = agent.MonitorOutcome;
        Tick(agent, 241, Window(240, 4, 0.5f));
        Check(ReferenceEquals(outcome, agent.MonitorOutcome), "Waiting tick replaced the outcome");
        agent.Initialize(agent.ActiveProfile, PersonalizationStrategy.CreateNeutral(), "new-session");
        Check(agent.MonitorDecision == null && agent.MonitorOutcome == null, "Prior session evidence leaked");
    }

    private static void BaselineConditions()
    {
        foreach (var mode in new[] { AudioRLPolicyMode.StaticPersonalized, AudioRLPolicyMode.RuleBasedAdaptive })
        {
            var agent = Warmup(mode);
            if (mode == AudioRLPolicyMode.StaticPersonalized) Near(agent.CurrentProposedAction.MaximumAbsoluteMagnitude, 0);
            else Check(agent.CurrentProposedAction.MaximumAbsoluteMagnitude > 0
                && agent.CurrentProposedAction.deltaIntensity <= 0 && agent.CurrentProposedAction.deltaTempo <= 0,
                "Rule did not choose a complete calming action");
            Near(agent.CurrentResidualAction.MaximumAbsoluteMagnitude, 0);
            Tick(agent, 121, Window(120, 2)); Tick(agent, 180, Window(180, 3));
            Tick(agent, 181, Window(180, 3)); Tick(agent, 240, Window(240, 4));
            Check(agent.OnlineUpdateCount == 0, "Baseline condition learned");
        }
    }

    private static void InvalidRuntimeOutcomes()
    {
        var agent = Warmup(); Tick(agent, 121, Window(120, 2), false);
        Check(agent.InvalidOutcomeCount == 1 && agent.OnlineUpdateCount == 0, "Emergency learned");
        Check(!agent.GetReplaySnapshot()[0].rewardValid && agent.GetReplaySnapshot()[0].reward == null, "Invalid reward was fabricated");
        agent = Warmup(); agent.SetControlContext(false, false, "Paused");
        Tick(agent, 240, Window(240, 4)); Check(agent.OnlineUpdateCount == 0, "Pause learned");
        agent = Warmup(); agent.InvalidatePendingOutcome("Visual or music content changed");
        Tick(agent, 240, Window(240, 4)); Check(agent.OnlineUpdateCount == 0, "Contaminated outcome learned");
    }

    private static void PlaybackAttribution()
    {
        var agent = Warmup(AudioRLPolicyMode.RuleBasedAdaptive);
        agent.ObservePlayback(Parameters());
        Tick(agent, 180, Window(180, 3)); Tick(agent, 240, Window(240, 4));
        Check(agent.RewardVersion == 0, "Reward accepted before playback reached target");
        agent.ObservePlayback(agent.CurrentTargetParameters);
        Tick(agent, 241, Window(240, 4)); Tick(agent, 300, Window(300, 5));
        Tick(agent, 360, Window(360, 6));
        Check(agent.RewardVersion == 1, "Clean outcome after actual transition did not resolve");
    }

    private static void DomainSwitch()
    {
        var agent = Agent();
        for (int i = 0; i <= 15; i += 5) Tick(agent, i, new SignalPacket(0.5f, 0.9f, i));
        Check(agent.OnlineUpdateCount > 0, "Simulation did not exercise online learning");
        Tick(agent, 60, Window(60));
        Check(agent.OnlineUpdateCount == 0 && agent.BaselineObservationCount == 1, "Synthetic training leaked into live domain");
    }

    private static void MissingPhysiologyReward()
    {
        var before = State(); before.signal = Window(120, 2);
        var after = State(); after.signal = Window(240, 4);
        before.signal.hasRmssd = false; before.signal.rmssd = 0;
        var reward = new AudioRLRewardCalculator(null, 0.08f).Compute(before, after, AudioRLAction.NoChange, 0);
        Near(reward.rmssdImprovement, 0);
    }

    private static void BaselineOverlap()
    {
        var baseline = new AudioSessionBaseline();
        baseline.Add(Window(60)); baseline.Add(Window(90, 2));
        Check(baseline.Count == 1, "Overlapping baseline samples counted twice");
        baseline.Add(Window(120, 3)); Check(baseline.Count == 2, "Clean baseline sample rejected");
    }

    private static void FrozenEvaluation()
    {
        var agent = Warmup();
        typeof(AudioRLAgent).GetField("allowOnlineLearning", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(agent, false);
        Tick(agent, 121, Window(120, 2)); Tick(agent, 180, Window(180, 3)); Tick(agent, 240, Window(240, 4));
        Check(agent.RewardVersion == 1 && agent.OnlineUpdateCount == 0, "Frozen evaluation trained the model");
        Check(!agent.GetReplaySnapshot()[0].onlineUpdateApplied, "Frozen evaluation log claims a training update");
    }

    private static void ReplayIsolation()
    {
        var replay = new AudioRLReplayBuffer(2);
        replay.Add(new AudioRLTransition { reward = new AudioRLRewardBreakdown { totalReward = 1 }, contextFeatures = new[] { 1d } });
        var snapshot = replay.Snapshot()[0]; snapshot.reward.totalReward = 99; snapshot.contextFeatures[0] = 99;
        Near(replay.Snapshot()[0].reward.totalReward, 1); Near(replay.Snapshot()[0].contextFeatures[0], 1);
    }

    private static void InvalidAndExpired()
    {
        var agent = Warmup(); var bad = Window(180, 3); bad.signalQuality = 0.1f;
        Tick(agent, 180, bad);
        Check(agent.OnlineUpdateCount == 0 && agent.InvalidOutcomeCount == 1, "Low-quality outcome trained");
        agent = Warmup(); Tick(agent, 400, Window(400, 7));
        Check(agent.OnlineUpdateCount == 0 && agent.InvalidOutcomeCount == 1, "Expired attribution trained");
    }

    private static void ContextLearning()
    {
        var model = new AudioLinUcbModel(2, 3, 0.25, 1);
        int early = 0, late = 0;
        for (int i = 0; i < 400; i++)
        {
            int preferred = i % 2 == 0 ? 1 : 2;
            double[] context = { 1, preferred == 1 ? -1 : 1 };
            int arm = model.Select(context, new[] { 0, 1, 2 }).Arm;
            double reward = arm == preferred ? 1 : arm == 0 ? 0 : -1;
            model.Observe(arm, context, reward);
            if (i < 20 && arm == preferred) early++;
            if (i >= 380 && arm == preferred) late++;
        }
        Check(late >= 18 && late > early, "Online model failed synthetic contextual-learning sanity test");
        Console.WriteLine("Synthetic sanity check: preferred choices first 20=" + early + ", last 20=" + late + ". This is not participant evidence.");
    }

    private static void ResponsivePreset()
    {
        var physiology = new LaminarVR.AdaptiveMeditation.Physiology.PhysiologyValidationConfiguration(
            "pilot", 1, 90, 30, 2, 0.001, 0.005, 0.5, 0.5, 8);
        var testPhysiology = ResponsiveTestingPreset.Apply(physiology);
        Near(physiology.MinimumDecisionSignalQuality, 0.5);
        Near(testPhysiology.MinimumDecisionSignalQuality, 0.35);
        Near(testPhysiology.MinimumRewardSignalQuality, 0.35);
        Near(testPhysiology.MinimumWindowDurationSeconds, physiology.MinimumWindowDurationSeconds);
        Near(testPhysiology.StaleAfterSeconds, physiology.StaleAfterSeconds);
        Near(testPhysiology.MaximumFutureClockSkewSeconds, physiology.MaximumFutureClockSkewSeconds);
        var timing = ResponsiveTestingPreset.Apply(new LaminarVR.AdaptiveMeditation.Session.SessionTimingConfiguration(
            "pilot", 1, 120, 900, 150, 75));
        var coordinator = ResponsiveTestingPreset.Apply(new LaminarVR.AdaptiveMeditation.Application.ProductionCoordinatorConfiguration(
            "pilot", 1, 60, 3, 1));
        var reward = ResponsiveTestingPreset.Apply(new LaminarVR.AdaptiveMeditation.Rewards.RewardPipelineConfiguration(
            "pilot", 1, LaminarVR.AdaptiveMeditation.Physiology.BaselineStandardDeviationMethod.Population,
            2, 0.01, 5, 3, 5, 120, 1, 0.35, 0.15, 0.1, 0, 2));
        Check(coordinator.TryValidateCompatibility(timing, testPhysiology, reward, out _), "Testing cadence is incompatible");
        Near(timing.DecisionIntervalSeconds, 15);
        Near(timing.AcclimatizationDurationSeconds, 120);
        Near(coordinator.ExpectedPhysiologyOutputIntervalSeconds, 5);
        Near(coordinator.SafetyLimits.MaximumTotalVariation, 1);
        Near(reward.SettlingSeconds, 5);
        Near(reward.MaximumAttributionWaitSeconds, 240);
        var model = new LaminarVR.AdaptiveMeditation.Policy.ContextualBandit.LinUcbModelConfiguration("pilot", 1, "f", 2, 1, 0.25);
        var testingModel = ResponsiveTestingPreset.Apply(model);
        Check(model.ModelVersion != testingModel.ModelVersion, "Testing visual model shares production identity");
        Near(testingModel.ExplorationCoefficient, 0.5);
        Near(model.ExplorationCoefficient, 0.25);
    }

    private static SignalPacket LowerReliability(double end, long sequence)
    {
        var signal = Window(end, sequence, 0.40f);
        signal.signalQuality = 0.4f;
        signal.confidence = 0.3f;
        return signal;
    }

    private static void ResponsiveAudio()
    {
        var production = Agent();
        var testing = Agent(responsiveTesting: true);
        Tick(production, 60, LowerReliability(60, 1));
        Tick(testing, 60, LowerReliability(60, 1));
        Tick(production, 120, LowerReliability(120, 2));
        Tick(testing, 120, LowerReliability(120, 2));
        Check(production.DecisionCount == 0 && production.CurrentSafetyMode == AudioRLSafetyMode.SignalQualityFreeze,
            "Production input gate was weakened");
        Check(testing.DecisionCount == 1 && testing.HasPendingOutcome, "Testing rejected an eligible lower-quality window");
        Tick(production, 180, Window(180, 3));
        Check(testing.ModelPath != production.ModelPath, "Testing audio model shares production persistence");
        bool rejected = false;
        try { testing.ConfigureResponsiveTesting(false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Testing preset changed during an initialized session");
        var bad = LowerReliability(180, 3); bad.signalQuality = 0.1f;
        Tick(testing, 180, bad);
        Check(!testing.HasPendingOutcome && testing.OnlineUpdateCount == 0, "Testing learned from unusable input");
    }

    private static void ResponsiveAttribution()
    {
        var coordinator = new AdaptationCoordinator();
        var agent = Agent(responsiveTesting: true);
        agent.BindCoordination(coordinator);
        Tick(agent, 60, LowerReliability(60, 1));
        Tick(agent, 120, LowerReliability(120, 2));
        Tick(agent, 121, LowerReliability(120, 2));
        Tick(agent, 135, LowerReliability(135, 3));
        Check(agent.DecisionCount == 1 && agent.OnlineUpdateCount == 0,
            "Short test cadence bypassed pending reward or accepted an overlapping window");
        var after = LowerReliability(190, 4); after.stress -= 0.01f;
        Tick(agent, 190, after);
        Check(agent.OnlineUpdateCount == 1 && coordinator.Status.CompletedTurns == 1,
            "Small stress change did not produce a valid test-policy reward");
        Check(agent.CurrentPolicyStatus.Contains(ResponsiveTestingPreset.Id), "Testing status is not identified");
        Tick(agent, 196, after);
        Check(agent.DecisionCount == 2 && agent.CurrentFinalSafeAction.MaximumAbsoluteMagnitude > 0,
            "Real audio exploration did not select a safe control change after learning");
    }
}
