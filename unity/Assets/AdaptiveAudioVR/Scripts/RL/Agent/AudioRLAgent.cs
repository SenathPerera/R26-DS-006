using System;
using System.Collections.Generic;
using System.IO;
using AdaptiveAudioVR.Core;
using LaminarVR.AdaptiveMeditation.Coordination;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    [DisallowMultipleComponent]
    public sealed class AudioRLAgent : MonoBehaviour
    {
        [Header("Whole Controller Study Condition")]
        [SerializeField] private AudioRLPolicyMode policyMode = AudioRLPolicyMode.PersonalizedBandit;
        [SerializeField] private bool allowOnlineLearning = true;
        [SerializeField] private bool allowSimulationLearning = true;
        [SerializeField] private bool persistPersonalizedModel = true;
        [SerializeField] private string environmentId = "japanese_temple_pond_garden";
        [SerializeField, Range(0f, 1f)] private float explorationAlpha = 0.25f;
        [SerializeField, Min(0.01f)] private float ridge = 1f;
        [SerializeField, Range(0.005f, 0.05f)] private float microActionDelta = 0.03f;

        [Header("Decision And Outcome Timing")]
        [SerializeField, Min(0.5f)] private float simulationDecisionIntervalSeconds = 5f;
        [SerializeField, Min(60f)] private float productionMinimumDecisionIntervalSeconds = 75f;
        [SerializeField, Min(2)] private int warmupObservationCount = 2;
        [SerializeField, Min(1)] private int episodeHorizon = 120;
        [SerializeField, Min(0f)] private float settlingSeconds = 5f;
        [SerializeField, Min(120f)] private float outcomeTimeoutSeconds = 240f;
        [SerializeField, Min(1f)] private float productionSignalTimeoutSeconds = 90f;
        [SerializeField, Min(0.5f)] private float simulationSignalTimeoutSeconds = 3f;

        [Header("Safety")]
        [SerializeField, Range(0.005f, 0.15f)] private float maximumActionDelta = 0.08f;
        [SerializeField, Range(0.005f, 0.15f)] private float maximumActionAcceleration = 0.05f;
        [SerializeField, Range(0.01f, 0.5f)] private float outputSlewPerSecond = 0.08f;
        [SerializeField, Range(0.05f, 0.6f)] private float maximumBaselineDistance = 0.30f;
        [SerializeField, Range(0f, 1f)] private float confidenceFreezeThreshold = 0.25f;
        [SerializeField, Range(0f, 1f)] private float lowConfidenceThreshold = 0.45f;
        [SerializeField, Range(0f, 1f)] private float signalQualityFreezeThreshold = 0.30f;
        [SerializeField, Range(0f, 1f)] private float lowSignalQualityThreshold = 0.60f;
        [SerializeField, Range(0f, 1f)] private float minimumAmbientMix = 0.15f;
        [SerializeField, Range(0f, 1f)] private float maximumMusicMix = 0.85f;

        [Header("Logging")]
        [SerializeField, Min(16)] private int replayBufferCapacity = 2048;
        [SerializeField] private AudioRLRewardWeights rewardWeights = new AudioRLRewardWeights();
        [SerializeField] private AudioRLTransitionLogger transitionLogger;

        public AudioProfile ActiveProfile { get; private set; }
        public PersonalizationStrategy ActiveStrategy { get; private set; }
        public AudioParameters PersonalizedBaseline { get; private set; }
        public AudioParameters CurrentParameters { get; private set; }
        public AudioParameters CurrentTargetParameters { get; private set; }
        public SignalPacket CurrentSignal { get; private set; }
        public AdaptiveControllerMode CurrentControllerMode { get; private set; } = AdaptiveControllerMode.Initialized;
        public AudioRLSafetyMode CurrentSafetyMode { get; private set; }
        public AudioRLPolicyMode CurrentPolicyMode => policyMode;
        public string CurrentPolicyStatus { get; private set; } = "Not initialized";
        public string CurrentActionName { get; private set; } = "Baseline hold";
        public string CurrentStateSummary { get; private set; } = "Waiting for observations";
        public string CurrentSafetyReason { get; private set; } = "Waiting for observations";
        public string CurrentLearningStatus { get; private set; } = "Waiting for a valid delayed outcome";
        public AudioRLAction CurrentRuleAction { get; private set; }
        public AudioRLAction CurrentProposedAction { get; private set; }
        // Kept for existing scene bindings. The active controller has no residual.
        public AudioRLAction CurrentResidualAction => AudioRLAction.NoChange;
        public AudioRLAction CurrentFinalSafeAction { get; private set; }
        public AudioRLRewardBreakdown CurrentRewardBreakdown { get; private set; } = AudioRLRewardBreakdown.Zero;
        public float CurrentReward => CurrentRewardBreakdown.totalReward;
        public int RewardVersion { get; private set; }
        public int ReplayBufferCount => replayBuffer?.Count ?? 0;
        public int DecisionCount { get; private set; }
        public AudioRLTransition MonitorDecision { get; private set; }
        public AudioRLTransition MonitorOutcome { get; private set; }
        public double MonitorDecisionAt { get; private set; }
        public double MonitorOutcomeAt { get; private set; }
        public int MonitorOutcomeDecision { get; private set; }
        public int OnlineUpdateCount => policy?.Model.UpdateCount ?? 0;
        public int InvalidOutcomeCount { get; private set; }
        public int BaselineObservationCount => baseline?.Count ?? 0;
        public bool IsInitialized => ActiveProfile != null;
        public bool IsUsingProductionPhysiology => CurrentSignal.hasPhysiologyWindow;
        public string SessionId { get; private set; }
        public string ModelPath => persistence?.FilePath ?? string.Empty;
        public string PersistenceStatus { get; private set; } = "Not loaded";

        private AudioOnlinePolicy policy;
        private AudioBanditPersistence persistence;
        private AudioRLSafetyFilter safetyFilter;
        private AudioRLRewardCalculator rewardCalculator;
        private AudioRLReplayBuffer replayBuffer;
        private AudioSessionBaseline baseline;
        private PendingDecision pending;
        private long coordinationLease;
        public AdaptationCoordinator Coordination { get; private set; }
        public bool HasPendingOutcome => pending != null;
        public bool GenerationPlaybackAvailable { get; private set; }

        public void SetGenerationPlaybackAvailable(bool available)
        {
            if (available == GenerationPlaybackAvailable) return;
            InvalidatePendingOutcome("Audio generation capability changed");
            SaveModel();
            productionDomain = null;
            GenerationPlaybackAvailable = available;
        }

        public bool IsPendingGenerationAction(string requestId) => pending != null
            && pending.generationRequired && pending.requestId == requestId
            && (Coordination == null || Coordination.IsCurrent(coordinationLease));

        public bool TryGetGenerationAction(out string requestId, out AudioParameters target)
        {
            requestId = pending?.requestId;
            target = pending != null ? pending.target : default;
            return GenerationPlaybackAvailable && IsPendingGenerationAction(requestId) && !pending.generationReady;
        }

        public bool ConfirmGenerationPlayback(string requestId)
        {
            if (!IsPendingGenerationAction(requestId) || pending.generationReady) return false;
            pending.generationReady = true;
            CurrentParameters = WithGeneratedControls(CurrentParameters, pending.target);
            appliedPlayback = WithGeneratedControls(appliedPlayback, pending.target);
            return true;
        }

        public void FailGenerationAction(string requestId, string reason)
        {
            if (IsPendingGenerationAction(requestId)) InvalidatePendingOutcome(reason);
        }

        public void ReconcileGeneratedPlayback(AudioParameters generated)
        {
            InvalidatePendingOutcome("Generated playback reconciled without reward");
            CurrentParameters = WithGeneratedControls(CurrentParameters, generated);
            CurrentTargetParameters = WithGeneratedControls(CurrentTargetParameters, generated);
            appliedPlayback = WithGeneratedControls(appliedPlayback, generated);
        }
        public bool ResponsiveTesting { get; private set; }
        public float MinimumInputQuality => ResponsiveTesting
            ? (float)ResponsiveTestingPreset.MinimumQuality : lowSignalQualityThreshold;
        public float MinimumInputConfidence => ResponsiveTesting
            ? (float)ResponsiveTestingPreset.MinimumAudioConfidence : lowConfidenceThreshold;
        private float ProductionDecisionInterval => ResponsiveTesting
            ? (float)ResponsiveTestingPreset.DecisionIntervalSeconds : productionMinimumDecisionIntervalSeconds;
        private float Exploration => ResponsiveTesting
            ? Mathf.Max(explorationAlpha, (float)ResponsiveTestingPreset.ExplorationCoefficient) : explorationAlpha;

        public void ConfigureResponsiveTesting(bool value)
        {
            if (IsInitialized && ResponsiveTesting != value)
                throw new InvalidOperationException("Select the testing preset before audio initialization.");
            ResponsiveTesting = value;
        }

        public void BindCoordination(AdaptationCoordinator coordinator)
        {
            if (ReferenceEquals(Coordination, coordinator)) return;
            InvalidatePendingOutcome("Shared coordinator changed");
            if ((Coordination == null) != (coordinator == null))
            {
                SaveModel();
                productionDomain = null;
            }
            Coordination = coordinator;
        }
        private AudioRLAction previousSafeAction;
        private SignalPacket previousObservation;
        private bool hasPreviousObservation;
        private bool? productionDomain;
        private bool adaptiveAllowed = true;
        private bool outcomesAllowed = true;
        private string gateReason = string.Empty;
        private bool hasPlaybackFeedback;
        private AudioParameters appliedPlayback;
        private double lastWindowEnd = double.MinValue;
        private double lastUsedPostEnd = double.MinValue;
        private double lastDecisionTime = double.NegativeInfinity;
        private double lastObservationTime = double.NegativeInfinity;
        private double resumeAfterUtc;
        private double now;
        private double utcNow;
        private int noveltyCount;
        private int conditionSegment;

        public void Initialize(AudioProfile profile, PersonalizationStrategy strategy, string relaySessionId = null)
        {
            InvalidatePendingOutcome("Audio profile/session reinitialized");
            SaveModel();
            ActiveProfile = profile;
            ActiveStrategy = strategy ?? PersonalizationStrategy.CreateNeutral();
            var safeBaseline = ActiveStrategy.ApplyTo(profile != null ? profile.ToBaselineParameters() : default).Clamp01();
            safeBaseline.musicMix = Mathf.Clamp(safeBaseline.musicMix, 0f, Mathf.Min(maximumMusicMix, 1f - minimumAmbientMix));
            safeBaseline.ambientMix = 1f - safeBaseline.musicMix;
            safeBaseline.brightness = Mathf.Min(safeBaseline.brightness, 0.90f);
            PersonalizedBaseline = safeBaseline;
            CurrentParameters = CurrentTargetParameters = PersonalizedBaseline;
            CurrentSignal = SignalPacket.CreateDefault();
            CurrentRewardBreakdown = AudioRLRewardBreakdown.Zero;
            CurrentRuleAction = CurrentProposedAction = CurrentFinalSafeAction = previousSafeAction = AudioRLAction.NoChange;
            DecisionCount = RewardVersion = InvalidOutcomeCount = noveltyCount = conditionSegment = 0;
            MonitorDecision = MonitorOutcome = null;
            MonitorDecisionAt = MonitorOutcomeAt = 0d;
            MonitorOutcomeDecision = 0;
            productionDomain = null;
            pending = null;
            persistence = null;
            hasPreviousObservation = hasPlaybackFeedback = false;
            lastWindowEnd = lastUsedPostEnd = double.MinValue;
            lastDecisionTime = lastObservationTime = double.NegativeInfinity;
            resumeAfterUtc = 0;
            baseline = new AudioSessionBaseline();
            microActionDelta = Mathf.Min(microActionDelta, maximumActionDelta, maximumActionAcceleration);
            policy = new AudioOnlinePolicy(microActionDelta, Exploration, ridge);
            safetyFilter = new AudioRLSafetyFilter(maximumActionDelta,
                ResponsiveTesting ? 0.10f : confidenceFreezeThreshold, MinimumInputConfidence,
                ResponsiveTesting ? 0.15f : signalQualityFreezeThreshold, MinimumInputQuality, maximumBaselineDistance,
                maximumActionAcceleration, minimumAmbientMix, maximumMusicMix);
            rewardCalculator = new AudioRLRewardCalculator(rewardWeights, maximumActionDelta);
            replayBuffer = new AudioRLReplayBuffer(replayBufferCapacity);
            SessionId = string.IsNullOrWhiteSpace(relaySessionId)
                ? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + "_audio"
                : relaySessionId;
            transitionLogger ??= GetComponent<AudioRLTransitionLogger>();
            if (transitionLogger == null) transitionLogger = gameObject.AddComponent<AudioRLTransitionLogger>();
            transitionLogger.StartSession(SessionId);
            RefreshPolicyStatus();
        }

        public void SetPolicyMode(AudioRLPolicyMode mode)
        {
            if (!Enum.IsDefined(typeof(AudioRLPolicyMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (mode == policyMode) return;
            InvalidatePendingOutcome("Controller condition changed");
            SaveModel();
            policyMode = mode;
            conditionSegment++;
            noveltyCount = 0;
            CurrentRewardBreakdown = AudioRLRewardBreakdown.Zero;
            CurrentTargetParameters = WithGeneratedControls(PersonalizedBaseline, CurrentParameters);
            previousSafeAction = AudioRLAction.NoChange;
            CurrentProposedAction = CurrentRuleAction = CurrentFinalSafeAction = AudioRLAction.NoChange;
            CurrentActionName = "Returning to personalized baseline";
            lastDecisionTime = double.NegativeInfinity;
            resumeAfterUtc = UtcSeconds();
            RefreshPolicyStatus();
        }

        public void SetControlContext(bool canAdapt, bool canObserveOutcomes, string reason)
        {
            if (outcomesAllowed && !canObserveOutcomes)
                InvalidatePendingOutcome(reason);
            if (!outcomesAllowed && canObserveOutcomes)
                resumeAfterUtc = UtcSeconds();
            adaptiveAllowed = canAdapt;
            outcomesAllowed = canObserveOutcomes;
            gateReason = reason ?? string.Empty;
        }

        public void ObservePlayback(AudioParameters applied)
        {
            appliedPlayback = applied;
            hasPlaybackFeedback = true;
        }

        public AudioParameters Evaluate(SignalPacket signal, float deltaTime, bool safeToRun) =>
            EvaluateAt(signal, deltaTime, safeToRun, Time.realtimeSinceStartupAsDouble, UtcSeconds());

        // Explicit clocks allow deterministic tests of cadence and reward attribution.
        public AudioParameters EvaluateAt(SignalPacket signal, float deltaTime, bool safeToRun, double monotonicSeconds, double utcSeconds)
        {
            if (!IsInitialized) return default;
            now = monotonicSeconds;
            utcNow = utcSeconds;
            CurrentSignal = signal;
            if (hasPlaybackFeedback) CurrentParameters = appliedPlayback;
            if (!safeToRun)
            {
                Hold("Emergency mute", AudioRLSafetyMode.EmergencyMuted);
                InvalidatePendingOutcome("Emergency mute");
                resumeAfterUtc = utcNow;
                return CurrentParameters;
            }
            float timeout = signal.hasPhysiologyWindow ? productionSignalTimeoutSeconds : simulationSignalTimeoutSeconds;
            if (Time.time - signal.timestamp > timeout)
            {
                Hold("Stale signal hold", AudioRLSafetyMode.StaleSignalRecovery);
                InvalidatePendingOutcome("Stale signal");
                resumeAfterUtc = utcNow;
                return CurrentParameters;
            }
            if (!AudioOutcomeGate.IsValidWindow(signal, utcNow, MinimumInputQuality, MinimumInputConfidence, out string invalid))
            {
                Hold(invalid, signal.signalQuality < MinimumInputQuality
                    ? AudioRLSafetyMode.SignalQualityFreeze : AudioRLSafetyMode.ConfidenceFreeze);
                InvalidatePendingOutcome(invalid);
                resumeAfterUtc = utcNow;
                return CurrentParameters;
            }
            EnsureDomain(signal.hasPhysiologyWindow);
            Coordination?.Tick(now, utcNow);
            if (pending != null && Coordination != null && !Coordination.IsCurrent(coordinationLease))
            {
                Hold("Coordinated measurement interrupted", AudioRLSafetyMode.Normal);
                InvalidatePendingOutcome("Coordinated measurement interrupted");
            }
            if (!outcomesAllowed)
            {
                Hold(gateReason, AudioRLSafetyMode.Normal);
                InvalidatePendingOutcome(gateReason);
                return CurrentParameters;
            }
            if (pending != null)
            {
                if (now - pending.startedAt > outcomeTimeoutSeconds)
                    InvalidatePendingOutcome("No eligible outcome arrived before timeout");
                else if (double.IsPositiveInfinity(pending.eligiblePostStartUtc) && AtTarget()
                    && (!pending.generationRequired || pending.generationReady))
                {
                    pending.eligiblePostStartUtc = utcNow + (signal.hasPhysiologyWindow ? settlingSeconds : 0);
                    pending.settledAt = now + (signal.hasPhysiologyWindow ? settlingSeconds : 0);
                    Coordination?.MarkTransitionCompleted(coordinationLease, utcNow,
                        signal.hasPhysiologyWindow ? settlingSeconds : 0);
                }
            }
            bool fresh = signal.hasPhysiologyWindow ? signal.windowEnd > lastWindowEnd
                : now - lastObservationTime >= simulationDecisionIntervalSeconds;
            if (fresh)
            {
                lastObservationTime = now;
                if (signal.hasPhysiologyWindow) lastWindowEnd = signal.windowEnd;
                AudioRLState state = BuildState(signal);
                CurrentStateSummary = state.ToSummary();
                if (baseline.Count < Mathf.Max(2, warmupObservationCount)) baseline.Add(signal);
                if (pending != null && now >= pending.settledAt
                    && AudioOutcomeGate.CanAttribute(pending.state.signal, signal, pending.eligiblePostStartUtc, lastUsedPostEnd, out _))
                    CompleteOutcome(state);
                bool freshAfterResume = !signal.hasPhysiologyWindow || signal.windowStart >= resumeAfterUtc;
                double interval = signal.hasPhysiologyWindow ? ProductionDecisionInterval : simulationDecisionIntervalSeconds;
                if (pending == null && adaptiveAllowed && baseline.Count >= Mathf.Max(2, warmupObservationCount)
                    && freshAfterResume && now - lastDecisionTime >= interval && AtTarget())
                    Decide(state);
                previousObservation = signal;
                hasPreviousObservation = true;
            }
            else if (Coordination != null && pending == null && adaptiveAllowed && AtTarget()
                && baseline.Count >= Mathf.Max(2, warmupObservationCount)
                && signal.windowStart >= resumeAfterUtc && now - lastDecisionTime >= ProductionDecisionInterval)
            {
                // A denied reservation must be retried without waiting for another sensor publication.
                Decide(BuildState(signal));
            }
            var generated = CurrentParameters;
            CurrentParameters = AudioParameters.MoveTowards(CurrentParameters, CurrentTargetParameters,
                outputSlewPerSecond * Mathf.Max(0, deltaTime));
            CurrentParameters = WithGeneratedControls(CurrentParameters, generated);
            RefreshPolicyStatus();
            return CurrentParameters;
        }

        public IReadOnlyList<AudioRLTransition> GetReplaySnapshot() => replayBuffer?.Snapshot() ?? Array.Empty<AudioRLTransition>();

        public void InvalidatePendingOutcome(string reason)
        {
            CurrentLearningStatus = "No update: " + reason;
            resumeAfterUtc = Math.Max(resumeAfterUtc, UtcSeconds());
            if (pending == null) return;
            LogOutcome(null, false, false, reason);
            InvalidOutcomeCount++;
            CurrentTargetParameters = CurrentParameters;
            pending = null;
            Coordination?.Release(coordinationLease, now, utcNow > 0 ? utcNow : UtcSeconds(), reason);
            coordinationLease = 0;
        }

        private void Decide(AudioRLState state)
        {
            if (Coordination != null && !Coordination.TryAcquire(AdaptationOwner.Audio,
                    SessionId + "-" + (DecisionCount + 1), state.signal.windowStart, state.signal.windowEnd,
                    now, utcNow, outcomeTimeoutSeconds, out coordinationLease))
            {
                CurrentLearningStatus = "Waiting for coordinated audio turn and clean physiology";
                return;
            }
            try { DecideReserved(state); }
            catch
            {
                Coordination?.Release(coordinationLease, now, utcNow, "Audio decision failed");
                coordinationLease = 0;
                throw;
            }
        }

        private void DecideReserved(AudioRLState state)
        {
            var eligible = new List<int> { 0 };
            for (int arm = 1; arm < AudioActionCatalog.Count; arm++)
            {
                var action = AudioActionCatalog.Action(arm, microActionDelta);
                if ((action.deltaDensity != 0 || action.deltaTempo != 0) && !GenerationPlaybackAvailable) continue;
                if (action.deltaTempo != 0 && AudioMusicalMapping.Bpm(state.currentParameters.tempo, ActiveStrategy.bpmOffset)
                    == AudioMusicalMapping.Bpm(state.currentParameters.tempo + action.deltaTempo, ActiveStrategy.bpmOffset)) continue;
                var filtered = safetyFilter.Apply(action, previousSafeAction, state, true, true);
                if (!filtered.interventionApplied && AudioActionCatalog.Same(action, filtered.finalSafeAction)) eligible.Add(arm);
            }
            double[] features = AudioBanditFeatures.Encode(state, maximumActionDelta);
            bool learningEnabled = allowOnlineLearning && (state.isProductionWindow || allowSimulationLearning);
            AudioBanditSelection selected = policy.Select(policyMode, state, features, eligible, explore: learningEnabled);
            CurrentProposedAction = AudioActionCatalog.Action(selected.Arm, microActionDelta);
            CurrentRuleAction = policyMode == AudioRLPolicyMode.RuleBasedAdaptive ? CurrentProposedAction : AudioRLAction.NoChange;
            var safe = safetyFilter.Apply(CurrentProposedAction, previousSafeAction, state, true, true);
            CurrentFinalSafeAction = safe.finalSafeAction;
            CurrentTargetParameters = safe.safeTarget;
            CurrentSafetyMode = safe.safetyMode;
            CurrentSafetyReason = safe.reason;
            CurrentControllerMode = ResolveControllerMode(state.signal);
            int executedArm = AudioActionCatalog.Match(safe.finalSafeAction, microActionDelta);
            CurrentActionName = AudioActionCatalog.Name(executedArm);
            pending = new PendingDecision
            {
                id = SessionId + "-" + (DecisionCount + 1), state = state.Snapshot(), features = features,
                proposedArm = selected.Arm, executedArm = executedArm, action = safe.finalSafeAction,
                proposal = CurrentProposedAction, mode = policyMode, segment = conditionSegment,
                safety = safe, selection = selected, startedAt = now,
                requestId = Guid.NewGuid().ToString("N"), target = safe.safeTarget,
                generationRequired = AudioMusicalMapping.RequiresGeneration(state.currentParameters, safe.safeTarget),
                eligiblePostStartUtc = double.PositiveInfinity, settledAt = double.PositiveInfinity
            };
            if (safe.finalSafeAction.MaximumAbsoluteMagnitude > 0.00001f) noveltyCount++;
            previousSafeAction = safe.finalSafeAction;
            lastDecisionTime = now;
            DecisionCount++;
            CurrentLearningStatus = pending.generationRequired
                ? "Awaiting reserved Lyria action playback before settling and delayed outcome"
                : "Awaiting transition completion and a fresh delayed outcome";
            LogOutcome(null, false, false, "Pending delayed outcome", "decision");
        }

        private void CompleteOutcome(AudioRLState next)
        {
            CurrentRewardBreakdown = rewardCalculator.Compute(pending.state, next, pending.action, noveltyCount, baseline);
            bool update = pending.mode == AudioRLPolicyMode.PersonalizedBandit && allowOnlineLearning
                && (next.isProductionWindow || allowSimulationLearning) && pending.executedArm >= 0
                && !pending.safety.interventionApplied;
            Action commit = () => { if (update) policy.Model.Observe(pending.executedArm, pending.features, CurrentReward); };
            if (Coordination == null) commit();
            else if (!Coordination.TryCommitOutcome(coordinationLease, next.signal.windowStart, next.signal.windowEnd, commit))
            {
                InvalidatePendingOutcome("Shared outcome reservation is no longer valid");
                return;
            }
            CurrentLearningStatus = update ? "Valid delayed outcome: personalized LinUCB updated"
                : "Valid delayed outcome: evaluation only; model unchanged";
            LogOutcome(next, true, update, CurrentLearningStatus);
            if (next.signal.hasPhysiologyWindow) lastUsedPostEnd = next.signal.windowEnd;
            RewardVersion++;
            pending = null;
            Coordination?.Release(coordinationLease, now, utcNow, "Audio turn finished");
            coordinationLease = 0;
            if (update) SaveModel();
        }

        private void LogOutcome(AudioRLState next, bool valid, bool updated, string reason, string eventType = "outcome")
        {
            var transition = new AudioRLTransition
            {
                sessionId = SessionId, userId = ActiveProfile.userId, createdUtc = DateTime.UtcNow.ToString("O"),
                eventType = eventType,
                decisionId = pending.id, conditionSegment = pending.segment,
                state = pending.state.Snapshot(), proposedPolicyAction = pending.proposal,
                ruleBaselineAction = pending.mode == AudioRLPolicyMode.RuleBasedAdaptive ? pending.proposal : AudioRLAction.NoChange,
                finalSafeAction = pending.action, reward = valid ? CurrentRewardBreakdown : null, nextState = next?.Snapshot(),
                policyMode = pending.mode.ToString(), policyStatus = pending.mode + " / whole controller"
                    + (ResponsiveTesting ? " / " + ResponsiveTestingPreset.Id : string.Empty),
                controllerMode = CurrentControllerMode.ToString(), safetyMode = pending.safety.safetyMode.ToString(),
                safetyReason = pending.safety.reason, usedProductionPhysiology = pending.state.isProductionWindow,
                rewardValid = valid, onlineUpdateApplied = updated, outcomeReason = reason,
                proposedArm = pending.proposedArm, executedArm = pending.executedArm,
                featureSchema = AudioBanditFeatures.Schema, contextFeatures = (double[])pending.features.Clone(),
                predictedReward = pending.selection.Mean, uncertainty = pending.selection.Uncertainty,
                selectionScore = pending.selection.Score, onlineUpdateCount = OnlineUpdateCount,
                eligiblePostStartUtc = double.IsPositiveInfinity(pending.eligiblePostStartUtc) ? 0 : pending.eligiblePostStartUtc
            };
            if (eventType == "decision")
            {
                MonitorDecision = transition;
                MonitorDecisionAt = utcNow;
            }
            if (eventType == "outcome")
            {
                MonitorOutcome = transition;
                MonitorOutcomeAt = utcNow;
                MonitorOutcomeDecision = DecisionCount;
                replayBuffer.Add(transition);
            }
            transitionLogger?.Log(transition);
        }

        private void EnsureDomain(bool production)
        {
            if (productionDomain == production) return;
            InvalidatePendingOutcome("Signal domain changed");
            SaveModel();
            productionDomain = production;
            baseline = new AudioSessionBaseline();
            hasPreviousObservation = false;
            lastWindowEnd = lastUsedPostEnd = double.MinValue;
            lastDecisionTime = lastObservationTime = double.NegativeInfinity;
            previousSafeAction = AudioRLAction.NoChange;
            policy = new AudioOnlinePolicy(microActionDelta, Exploration, ridge);
            float[] settings = { microActionDelta, maximumActionDelta, maximumActionAcceleration, maximumBaselineDistance,
                MinimumInputConfidence, MinimumInputQuality, minimumAmbientMix, maximumMusicMix,
                Exploration, ridge, ProductionDecisionInterval, settlingSeconds, outcomeTimeoutSeconds };
            string configuration = JsonUtility.ToJson(rewardWeights) + "|" + string.Join("|", Array.ConvertAll(settings,
                value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            if (Coordination != null) configuration += "|" + AdaptationCoordinator.SchemaVersion;
            configuration += "|" + AudioMusicalMapping.Schema + "|generation=" + GenerationPlaybackAvailable;
            if (ResponsiveTesting) configuration += "|" + ResponsiveTestingPreset.Id;
            persistence = new AudioBanditPersistence(Path.Combine(Application.persistentDataPath, "AudioOnlineModels"),
                ActiveProfile.userId, environmentId, !production, configuration);
            if (persistPersonalizedModel && !string.IsNullOrWhiteSpace(ActiveProfile.userId))
            {
                persistence.TryLoad(policy.Model, out string status);
                PersistenceStatus = status;
            }
        }

        private AudioRLState BuildState(SignalPacket signal) => new AudioRLState
        {
            userId = ActiveProfile.userId, signal = signal,
            stressTrend = hasPreviousObservation ? signal.stress - previousObservation.stress : 0,
            confidenceTrend = hasPreviousObservation ? signal.confidence - previousObservation.confidence : 0,
            heartRateTrend = hasPreviousObservation && signal.hasHeartRate && previousObservation.hasHeartRate
                ? signal.heartRate - previousObservation.heartRate : 0,
            rmssdTrend = hasPreviousObservation && signal.hasRmssd && previousObservation.hasRmssd
                ? signal.rmssd - previousObservation.rmssd : 0,
            currentParameters = CurrentParameters, personalizedBaseline = PersonalizedBaseline,
            preferenceEncoding = AudioRLStateEncoder.BuildPreferenceEncoding(ActiveProfile, PersonalizedBaseline),
            recentSafeAction = previousSafeAction,
            sessionProgress = Mathf.Clamp01(DecisionCount / (float)Mathf.Max(1, episodeHorizon)),
            timeSinceLastActionSeconds = double.IsNegativeInfinity(lastDecisionTime) ? 0 : (float)(now - lastDecisionTime),
            noveltyCount = noveltyCount, decisionIndex = DecisionCount, isProductionWindow = signal.hasPhysiologyWindow
        };

        private bool AtTarget() => AudioActionCatalog.Same(AudioRLAction.Between(CurrentParameters, CurrentTargetParameters), AudioRLAction.NoChange, 0.002f);

        private static AudioParameters WithGeneratedControls(AudioParameters local, AudioParameters generated)
        {
            local.density = generated.density;
            local.tempo = generated.tempo;
            return local;
        }

        private void Hold(string reason, AudioRLSafetyMode mode)
        {
            CurrentTargetParameters = CurrentParameters;
            CurrentProposedAction = CurrentRuleAction = CurrentFinalSafeAction = previousSafeAction = AudioRLAction.NoChange;
            CurrentActionName = "Hold: " + reason;
            CurrentSafetyMode = mode;
            CurrentControllerMode = mode == AudioRLSafetyMode.Normal ? AdaptiveControllerMode.Initialized
                : AdaptiveControllerMode.LowConfidenceDampened;
            CurrentSafetyReason = reason;
        }

        private void RefreshPolicyStatus()
        {
            string source = productionDomain == true ? "physiology" : "simulation";
            bool learningEnabled = allowOnlineLearning && (productionDomain == true || allowSimulationLearning);
            CurrentPolicyStatus = policyMode == AudioRLPolicyMode.PersonalizedBandit
                ? $"Personalized LinUCB | {source} | Updates {OnlineUpdateCount} | {(learningEnabled ? "online" : "evaluation frozen")}"
                : policyMode + " | evaluation only";
            if (ResponsiveTesting) CurrentPolicyStatus += " | " + ResponsiveTestingPreset.Id;
        }

        private static AdaptiveControllerMode ResolveControllerMode(SignalPacket signal) => signal.stress >= 0.65f
            ? AdaptiveControllerMode.HighStressAdaptive : signal.stress <= 0.35f
                ? AdaptiveControllerMode.LowStressCalming : AdaptiveControllerMode.MidRangeStabilizing;

        private void SaveModel()
        {
            if (!persistPersonalizedModel || persistence == null || policy == null || policy.Model.UpdateCount == 0) return;
            persistence.TrySave(policy.Model, out string status);
            PersistenceStatus = status;
        }

        private void OnDisable()
        {
            InvalidatePendingOutcome("Audio agent disabled");
            SaveModel();
        }

        private static double UtcSeconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;

        private sealed class PendingDecision
        {
            public string id;
            public AudioRLState state;
            public double[] features;
            public int proposedArm;
            public int executedArm;
            public AudioRLAction action;
            public AudioRLAction proposal;
            public AudioRLPolicyMode mode;
            public int segment;
            public AudioRLSafetyResult safety;
            public AudioBanditSelection selection;
            public double startedAt;
            public double eligiblePostStartUtc;
            public double settledAt;
            public string requestId;
            public AudioParameters target;
            public bool generationRequired;
            public bool generationReady;
        }
    }
}
