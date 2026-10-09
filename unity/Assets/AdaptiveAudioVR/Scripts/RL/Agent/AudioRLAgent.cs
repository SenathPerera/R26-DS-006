using System;
using System.Collections.Generic;
using System.IO;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    public enum AudioRLPolicyMode
    {
        RuleOnly,
        PpoResidual
    }

    [DisallowMultipleComponent]
    public sealed class AudioRLAgent : MonoBehaviour
    {
        [Header("Policy")]
        [SerializeField] private AudioRLPolicyMode policyMode = AudioRLPolicyMode.PpoResidual;
        [SerializeField] private string directPpoPolicyFileName = "ppo_seed_37_unity_network.json";
        [SerializeField] private string importedPolicyFileName = "ppo_seed_37_unity_policy.json";
        [SerializeField, Range(1, 32)] private int importedPolicyNeighbors = 8;

        [Header("Decision Cadence")]
        [SerializeField, Min(0.5f)] private float simulationDecisionIntervalSeconds = 5f;
        [SerializeField, Min(1f)] private float productionMinimumDecisionIntervalSeconds = 55f;
        [SerializeField, Min(0)] private int warmupObservationCount = 1;
        [SerializeField, Min(1)] private int episodeHorizon = 120;
        [SerializeField, Min(1f)] private float productionSignalTimeoutSeconds = 120f;
        [SerializeField, Min(0.5f)] private float simulationSignalTimeoutSeconds = 3f;

        [Header("Action Bounds")]
        [SerializeField, Range(0.005f, 0.15f)] private float maximumActionDelta = 0.08f;
        [SerializeField, Range(0.005f, 0.15f)] private float maximumActionAcceleration = 0.05f;
        [SerializeField, Range(0.01f, 0.5f)] private float outputSlewPerSecond = 0.08f;
        [SerializeField, Range(0.05f, 0.6f)] private float maximumBaselineDistance = 0.30f;

        [Header("Safety Thresholds")]
        [SerializeField, Range(0f, 1f)] private float confidenceFreezeThreshold = 0.25f;
        [SerializeField, Range(0f, 1f)] private float lowConfidenceThreshold = 0.45f;
        [SerializeField, Range(0f, 1f)] private float signalQualityFreezeThreshold = 0.30f;
        [SerializeField, Range(0f, 1f)] private float lowSignalQualityThreshold = 0.60f;
        [SerializeField, Range(0f, 1f)] private float minimumAmbientMix = 0.15f;
        [SerializeField, Range(0f, 1f)] private float maximumMusicMix = 0.85f;

        [Header("Rule Baseline")]
        [SerializeField, Range(0f, 1f)] private float highStressThreshold = 0.65f;
        [SerializeField, Range(0f, 1f)] private float lowStressThreshold = 0.35f;

        [Header("Learning Readiness")]
        [SerializeField, Min(16)] private int replayBufferCapacity = 2048;
        [SerializeField] private AudioRLRewardWeights rewardWeights = new AudioRLRewardWeights();
        [SerializeField] private AudioRLTransitionLogger transitionLogger;
        [SerializeField] private bool logPolicyLoad = true;

        public AudioProfile ActiveProfile { get; private set; }
        public PersonalizationStrategy ActiveStrategy { get; private set; }
        public AudioParameters PersonalizedBaseline { get; private set; }
        public AudioParameters CurrentParameters { get; private set; }
        public AudioParameters CurrentTargetParameters { get; private set; }
        public SignalPacket CurrentSignal { get; private set; }
        public AdaptiveControllerMode CurrentControllerMode { get; private set; } = AdaptiveControllerMode.Initialized;
        public AudioRLSafetyMode CurrentSafetyMode { get; private set; } = AudioRLSafetyMode.Normal;
        public AudioRLPolicyMode CurrentPolicyMode => policyMode;
        public string CurrentPolicyStatus { get; private set; } = "Not initialized";
        public string CurrentActionName { get; private set; } = "Warmup hold";
        public string CurrentStateSummary { get; private set; } = "No state observed yet.";
        public string CurrentSafetyReason { get; private set; } = "No safety decision yet.";
        public AudioRLAction CurrentRuleAction { get; private set; }
        public AudioRLAction CurrentResidualAction { get; private set; }
        public AudioRLAction CurrentFinalSafeAction { get; private set; }
        public AudioRLRewardBreakdown CurrentRewardBreakdown { get; private set; } = AudioRLRewardBreakdown.Zero;
        public float CurrentReward => CurrentRewardBreakdown.totalReward;
        public int RewardVersion { get; private set; }
        public int ReplayBufferCount => replayBuffer != null ? replayBuffer.Count : 0;
        public int DecisionCount => decisionCount;
        public bool IsInitialized => ActiveProfile != null;
        public bool IsUsingProductionPhysiology => CurrentSignal.hasPhysiologyWindow;
        public string SessionId => sessionId;

        private readonly Queue<AudioRLAction> recentResidualActions = new Queue<AudioRLAction>(3);
        private PpoDirectResidualPolicy directPpoPolicy;
        private PpoSampledResidualPolicy sampledPpoPolicy;
        private IAudioRLPolicy activeResidualPolicy;
        private AudioRuleBaselinePolicy rulePolicy;
        private AudioRLSafetyFilter safetyFilter;
        private AudioRLRewardCalculator rewardCalculator;
        private AudioRLReplayBuffer replayBuffer;
        private PendingDecision pendingDecision;
        private SignalPacket previousDecisionSignal;
        private bool hasPreviousDecisionSignal;
        private AudioRLAction previousSafeAction;
        private int observationCount;
        private int decisionCount;
        private int noveltyCount;
        private float nextSimulationDecisionTime;
        private float lastDecisionUnityTime = float.MinValue;
        private double lastProcessedWindowEnd = double.MinValue;
        private string sessionId;

        public void Initialize(AudioProfile profile, PersonalizationStrategy strategy)
        {
            ActiveProfile = profile;
            ActiveStrategy = strategy ?? PersonalizationStrategy.CreateNeutral();
            PersonalizedBaseline = ActiveStrategy.ApplyTo(profile != null ? profile.ToBaselineParameters() : default);
            PersonalizedBaseline.NormalizeMix();
            CurrentParameters = PersonalizedBaseline;
            CurrentTargetParameters = PersonalizedBaseline;
            CurrentSignal = SignalPacket.CreateDefault();
            CurrentControllerMode = AdaptiveControllerMode.Initialized;
            CurrentSafetyMode = AudioRLSafetyMode.Normal;
            CurrentRewardBreakdown = AudioRLRewardBreakdown.Zero;
            CurrentRuleAction = AudioRLAction.NoChange;
            CurrentResidualAction = AudioRLAction.NoChange;
            CurrentFinalSafeAction = AudioRLAction.NoChange;
            CurrentActionName = "Warmup hold";
            CurrentSafetyReason = "Waiting for the first complete observation.";
            CurrentStateSummary = "Waiting for the first complete observation.";

            observationCount = 0;
            decisionCount = 0;
            noveltyCount = 0;
            RewardVersion = 0;
            hasPreviousDecisionSignal = false;
            previousSafeAction = AudioRLAction.NoChange;
            pendingDecision = null;
            recentResidualActions.Clear();
            for (int i = 0; i < 3; i++)
            {
                recentResidualActions.Enqueue(AudioRLAction.NoChange);
            }

            nextSimulationDecisionTime = Time.time;
            lastDecisionUnityTime = float.MinValue;
            lastProcessedWindowEnd = double.MinValue;
            sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{SanitizeId(profile != null ? profile.userId : "unknown")}";

            rulePolicy = new AudioRuleBaselinePolicy(lowConfidenceThreshold, highStressThreshold, lowStressThreshold);
            safetyFilter = new AudioRLSafetyFilter(
                maximumActionDelta,
                confidenceFreezeThreshold,
                lowConfidenceThreshold,
                signalQualityFreezeThreshold,
                lowSignalQualityThreshold,
                maximumBaselineDistance,
                maximumActionAcceleration,
                minimumAmbientMix,
                maximumMusicMix);
            rewardCalculator = new AudioRLRewardCalculator(rewardWeights, maximumActionDelta);
            replayBuffer = new AudioRLReplayBuffer(replayBufferCapacity);

            transitionLogger ??= GetComponent<AudioRLTransitionLogger>();
            if (transitionLogger == null)
            {
                transitionLogger = gameObject.AddComponent<AudioRLTransitionLogger>();
            }
            transitionLogger.StartSession(sessionId);

            directPpoPolicy = new PpoDirectResidualPolicy();
            sampledPpoPolicy = new PpoSampledResidualPolicy(importedPolicyNeighbors);
            activeResidualPolicy = null;
            string directPath = GetDefaultDirectPpoPolicyPath();
            string sampledPath = GetDefaultImportedPolicyPath();
            string loadError;

            if (directPpoPolicy.TryLoad(directPath, out string directError))
            {
                activeResidualPolicy = directPpoPolicy;
                CurrentPolicyStatus = policyMode == AudioRLPolicyMode.PpoResidual
                    ? directPpoPolicy.DisplayName
                    : "Rule-only mode; direct PPO policy is loaded but inactive";
                if (logPolicyLoad)
                {
                    Debug.Log($"[AudioRLAgent] Loaded {directPpoPolicy.DisplayName} from {directPath}.", this);
                }
            }
            else if (sampledPpoPolicy.TryLoad(sampledPath, out string sampledError))
            {
                activeResidualPolicy = sampledPpoPolicy;
                loadError = $"Direct PPO load failed ({directError}); using sampled fallback.";
                CurrentPolicyStatus = policyMode == AudioRLPolicyMode.PpoResidual
                    ? $"{sampledPpoPolicy.DisplayName} | {loadError}"
                    : "Rule-only mode; sampled PPO fallback is loaded but inactive";
                Debug.LogWarning($"[AudioRLAgent] {loadError}", this);
            }
            else
            {
                loadError = $"Direct PPO load failed: {directError}; sampled fallback failed: {sampledError}";
                CurrentPolicyStatus = $"Rule-only fallback; {loadError}";
                Debug.LogWarning($"[AudioRLAgent] {CurrentPolicyStatus}", this);
            }
        }

        public AudioParameters Evaluate(SignalPacket signal, float deltaTime, bool safeToRun)
        {
            if (!IsInitialized)
            {
                return CurrentParameters;
            }

            CurrentSignal = signal;
            float timeout = signal.hasPhysiologyWindow ? productionSignalTimeoutSeconds : simulationSignalTimeoutSeconds;
            bool signalIsRecent = signal.IsRecent(timeout);

            if (!safeToRun)
            {
                EnterEmergencyHold(signal);
            }
            else if (!signalIsRecent)
            {
                EnterStaleSignalRecovery(signal);
            }
            else if (ShouldProcessObservation(signal))
            {
                ProcessObservation(signal, safeToRun, signalIsRecent);
            }

            float maximumStep = outputSlewPerSecond * Mathf.Max(0f, deltaTime);
            CurrentParameters = AudioParameters.MoveTowards(CurrentParameters, CurrentTargetParameters, maximumStep);
            return CurrentParameters.Clamp01();
        }

        public void SetPolicyMode(AudioRLPolicyMode mode)
        {
            policyMode = mode;
            CurrentPolicyStatus = mode == AudioRLPolicyMode.PpoResidual && activeResidualPolicy != null && activeResidualPolicy.IsReady
                ? activeResidualPolicy.DisplayName
                : "Rule-only safe baseline";
        }

        public string GetDefaultDirectPpoPolicyPath()
        {
            return Path.Combine(
                Application.streamingAssetsPath,
                "AdaptiveAudioVR",
                "Training",
                directPpoPolicyFileName);
        }

        public string GetDefaultImportedPolicyPath()
        {
            return Path.Combine(
                Application.streamingAssetsPath,
                "AdaptiveAudioVR",
                "Training",
                importedPolicyFileName);
        }

        public IReadOnlyList<AudioRLTransition> GetReplaySnapshot()
        {
            return replayBuffer != null ? replayBuffer.Snapshot() : Array.Empty<AudioRLTransition>();
        }

        private bool ShouldProcessObservation(SignalPacket signal)
        {
            if (signal.hasPhysiologyWindow)
            {
                bool hasUnprocessedWindow = signal.windowEnd > lastProcessedWindowEnd + 0.001d;
                bool cadenceReady = lastDecisionUnityTime == float.MinValue
                                    || Time.time - lastDecisionUnityTime >= productionMinimumDecisionIntervalSeconds;
                bool isValidPostWindow = pendingDecision == null
                                         || signal.IsNonOverlappingAfter(pendingDecision.state.signal);
                return hasUnprocessedWindow && cadenceReady && isValidPostWindow;
            }

            return Time.time >= nextSimulationDecisionTime;
        }

        private void ProcessObservation(SignalPacket signal, bool safeToRun, bool signalIsRecent)
        {
            AudioRLState state = BuildState(signal);
            observationCount++;

            if (pendingDecision != null)
            {
                CompletePendingTransition(state);
            }

            if (observationCount <= warmupObservationCount)
            {
                CurrentActionName = $"Warmup hold ({observationCount}/{warmupObservationCount})";
                CurrentPolicyStatus = ResolvePolicyStatus();
                CurrentStateSummary = state.ToSummary();
                CurrentControllerMode = AdaptiveControllerMode.Initialized;
                CurrentSafetyMode = AudioRLSafetyMode.Normal;
                CurrentSafetyReason = "Collecting an initial observation before adapting audio.";
                MarkObservationProcessed(signal);
                return;
            }

            CurrentRuleAction = rulePolicy.GetAction(state, maximumActionDelta);
            CurrentResidualAction = policyMode == AudioRLPolicyMode.PpoResidual && activeResidualPolicy != null && activeResidualPolicy.IsReady
                ? activeResidualPolicy.GetResidualAction(state)
                : AudioRLAction.NoChange;

            AudioRLAction combined = CurrentRuleAction + CurrentResidualAction;
            AudioRLSafetyResult safety = safetyFilter.Apply(combined, previousSafeAction, state, safeToRun, signalIsRecent);
            CurrentFinalSafeAction = safety.finalSafeAction;
            CurrentTargetParameters = safety.safeTarget;
            CurrentSafetyMode = safety.safetyMode;
            CurrentSafetyReason = safety.reason;
            CurrentControllerMode = ResolveControllerMode(signal, safety.safetyMode);
            CurrentActionName = DescribeAction(CurrentFinalSafeAction, policyMode == AudioRLPolicyMode.PpoResidual && activeResidualPolicy != null && activeResidualPolicy.IsReady);
            CurrentPolicyStatus = ResolvePolicyStatus();
            CurrentStateSummary = state.ToSummary();

            if (CurrentResidualAction.MeanAbsoluteMagnitude > maximumActionDelta * 0.20f)
            {
                noveltyCount++;
            }

            StoreRecentResidual(CurrentResidualAction);
            pendingDecision = safeToRun
                ? new PendingDecision
                {
                    state = state.Snapshot(),
                    ruleAction = CurrentRuleAction,
                    residualAction = CurrentResidualAction,
                    safeAction = CurrentFinalSafeAction,
                    policyMode = policyMode.ToString(),
                    policyStatus = CurrentPolicyStatus,
                    controllerMode = CurrentControllerMode.ToString(),
                    safetyMode = CurrentSafetyMode.ToString(),
                    safetyReason = CurrentSafetyReason
                }
                : null;

            previousSafeAction = CurrentFinalSafeAction;
            decisionCount++;
            MarkObservationProcessed(signal);
        }

        private void CompletePendingTransition(AudioRLState nextState)
        {
            CurrentRewardBreakdown = rewardCalculator.Compute(
                pendingDecision.state,
                nextState,
                pendingDecision.safeAction,
                noveltyCount);

            AudioRLTransition transition = new AudioRLTransition
            {
                sessionId = sessionId,
                userId = ActiveProfile != null ? ActiveProfile.userId : "unknown",
                createdUtc = DateTime.UtcNow.ToString("O"),
                state = pendingDecision.state.Snapshot(),
                ruleBaselineAction = pendingDecision.ruleAction,
                residualPolicyAction = pendingDecision.residualAction,
                finalSafeAction = pendingDecision.safeAction,
                reward = CurrentRewardBreakdown,
                nextState = nextState.Snapshot(),
                policyMode = pendingDecision.policyMode,
                policyStatus = pendingDecision.policyStatus,
                controllerMode = pendingDecision.controllerMode,
                safetyMode = pendingDecision.safetyMode,
                safetyReason = pendingDecision.safetyReason,
                usedProductionPhysiology = pendingDecision.state.signal.hasPhysiologyWindow && nextState.signal.hasPhysiologyWindow
            };

            replayBuffer.Add(transition);
            transitionLogger?.Log(transition);
            RewardVersion++;
            pendingDecision = null;
        }

        private AudioRLState BuildState(SignalPacket signal)
        {
            float stressTrend = hasPreviousDecisionSignal ? signal.stress - previousDecisionSignal.stress : 0f;
            float confidenceTrend = hasPreviousDecisionSignal ? signal.confidence - previousDecisionSignal.confidence : 0f;
            float heartRateTrend = hasPreviousDecisionSignal && signal.hasPhysiologyWindow
                ? signal.heartRate - previousDecisionSignal.heartRate
                : 0f;
            float rmssdTrend = hasPreviousDecisionSignal && signal.hasPhysiologyWindow
                ? signal.rmssd - previousDecisionSignal.rmssd
                : 0f;

            return new AudioRLState
            {
                userId = ActiveProfile != null ? ActiveProfile.userId : "unknown",
                signal = signal,
                stressTrend = stressTrend,
                confidenceTrend = confidenceTrend,
                heartRateTrend = heartRateTrend,
                rmssdTrend = rmssdTrend,
                currentParameters = CurrentParameters,
                personalizedBaseline = PersonalizedBaseline,
                preferenceEncoding = AudioRLStateEncoder.BuildPreferenceEncoding(ActiveProfile, PersonalizedBaseline),
                recentMeanResidualAction = GetMeanRecentResidual(),
                sessionProgress = Mathf.Clamp01(decisionCount / (float)Mathf.Max(1, episodeHorizon)),
                timeSinceLastActionSeconds = lastDecisionUnityTime == float.MinValue ? 0f : Mathf.Max(0f, Time.time - lastDecisionUnityTime),
                noveltyCount = noveltyCount,
                decisionIndex = decisionCount,
                isProductionWindow = signal.hasPhysiologyWindow
            };
        }

        private void EnterEmergencyHold(SignalPacket signal)
        {
            CurrentTargetParameters = CurrentParameters;
            CurrentRuleAction = AudioRLAction.NoChange;
            CurrentResidualAction = AudioRLAction.NoChange;
            CurrentFinalSafeAction = AudioRLAction.NoChange;
            CurrentControllerMode = AdaptiveControllerMode.LowConfidenceDampened;
            CurrentSafetyMode = AudioRLSafetyMode.EmergencyMuted;
            CurrentSafetyReason = "Emergency mute is active; pending reward attribution was cancelled.";
            CurrentActionName = "Emergency hold";
            CurrentStateSummary = BuildState(signal).ToSummary();
            previousSafeAction = AudioRLAction.NoChange;
            pendingDecision = null;
        }

        private void EnterStaleSignalRecovery(SignalPacket signal)
        {
            AudioRLState state = BuildState(signal);
            AudioRLSafetyResult recovery = safetyFilter.Apply(
                AudioRLAction.NoChange,
                previousSafeAction,
                state,
                true,
                false);
            CurrentTargetParameters = recovery.safeTarget;
            CurrentRuleAction = AudioRLAction.NoChange;
            CurrentResidualAction = AudioRLAction.NoChange;
            CurrentFinalSafeAction = recovery.finalSafeAction;
            CurrentControllerMode = AdaptiveControllerMode.LowConfidenceDampened;
            CurrentSafetyMode = recovery.safetyMode;
            CurrentSafetyReason = recovery.reason + " Pending reward attribution was cancelled.";
            CurrentActionName = "Stale signal baseline recovery";
            CurrentStateSummary = state.ToSummary();
            previousSafeAction = recovery.finalSafeAction;
            pendingDecision = null;
        }

        private void MarkObservationProcessed(SignalPacket signal)
        {
            previousDecisionSignal = signal;
            hasPreviousDecisionSignal = true;
            lastDecisionUnityTime = Time.time;
            nextSimulationDecisionTime = Time.time + Mathf.Max(0.5f, simulationDecisionIntervalSeconds);
            if (signal.hasPhysiologyWindow)
            {
                lastProcessedWindowEnd = signal.windowEnd;
            }
        }

        private void StoreRecentResidual(AudioRLAction action)
        {
            if (recentResidualActions.Count >= 3)
            {
                recentResidualActions.Dequeue();
            }

            recentResidualActions.Enqueue(action);
        }

        private AudioRLAction GetMeanRecentResidual()
        {
            if (recentResidualActions.Count == 0)
            {
                return AudioRLAction.NoChange;
            }

            float[] mean = new float[7];
            foreach (AudioRLAction action in recentResidualActions)
            {
                float[] values = action.ToArray();
                for (int i = 0; i < mean.Length; i++)
                {
                    mean[i] += values[i];
                }
            }

            for (int i = 0; i < mean.Length; i++)
            {
                mean[i] /= recentResidualActions.Count;
            }

            return AudioRLAction.FromArray(mean);
        }

        private string ResolvePolicyStatus()
        {
            if (policyMode == AudioRLPolicyMode.RuleOnly)
            {
                return "Rule-only safe baseline";
            }

            return activeResidualPolicy != null && activeResidualPolicy.IsReady
                ? activeResidualPolicy.DisplayName
                : "Rule-only fallback; PPO-derived residual policy unavailable";
        }

        private static AdaptiveControllerMode ResolveControllerMode(SignalPacket signal, AudioRLSafetyMode safetyMode)
        {
            if (safetyMode == AudioRLSafetyMode.ConfidenceFreeze
                || safetyMode == AudioRLSafetyMode.LowConfidenceDampened
                || safetyMode == AudioRLSafetyMode.SignalQualityFreeze
                || safetyMode == AudioRLSafetyMode.LowSignalQualityDampened
                || safetyMode == AudioRLSafetyMode.StaleSignalRecovery)
            {
                return AdaptiveControllerMode.LowConfidenceDampened;
            }

            if (signal.stress >= 0.65f)
            {
                return AdaptiveControllerMode.HighStressAdaptive;
            }

            if (signal.stress <= 0.35f)
            {
                return AdaptiveControllerMode.LowStressCalming;
            }

            return AdaptiveControllerMode.MidRangeStabilizing;
        }

        private static string DescribeAction(AudioRLAction action, bool usedPpo)
        {
            float[] values = action.ToArray();
            string[] positive = { "Increase intensity", "Increase density", "Increase brightness", "Increase tempo", "Lengthen fade", "Increase music", "Increase ambience" };
            string[] negative = { "Reduce intensity", "Reduce density", "Darken", "Reduce tempo", "Shorten fade", "Reduce music", "Reduce ambience" };
            int dominantIndex = 0;
            float dominantMagnitude = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                float magnitude = Mathf.Abs(values[i]);
                if (magnitude > dominantMagnitude)
                {
                    dominantMagnitude = magnitude;
                    dominantIndex = i;
                }
            }

            string prefix = usedPpo ? "PPO residual + rule: " : "Rule: ";
            if (dominantMagnitude <= 0.004f)
            {
                return prefix + "hold stable";
            }

            return prefix + (values[dominantIndex] >= 0f ? positive[dominantIndex] : negative[dominantIndex]);
        }

        private static string SanitizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value.Replace(' ', '_');
        }

        [Serializable]
        private sealed class PendingDecision
        {
            public AudioRLState state;
            public AudioRLAction ruleAction;
            public AudioRLAction residualAction;
            public AudioRLAction safeAction;
            public string policyMode;
            public string policyStatus;
            public string controllerMode;
            public string safetyMode;
            public string safetyReason;
        }
    }
}
