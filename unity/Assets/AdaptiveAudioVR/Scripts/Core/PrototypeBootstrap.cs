using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Controller;
using AdaptiveAudioVR.Integration;
using AdaptiveAudioVR.Logging;
using AdaptiveAudioVR.Preference;
using AdaptiveAudioVR.Profile;
using AdaptiveAudioVR.RL;
using AdaptiveAudioVR.RL.Agent;
using AdaptiveAudioVR.Safety;
using AdaptiveAudioVR.Signals;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Coordination;
using LaminarVR.AdaptiveMeditation.Session;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    public class PrototypeBootstrap : MonoBehaviour
    {
        [Header("Subsystem References")]
        [SerializeField] private PreferenceManager preferenceManager;
        [SerializeField] private ProfileEngine profileEngine;
        [SerializeField] private SignalSimulator signalSimulator;
        [SerializeField] private ComponentBStressSignalReceiver componentBSignalReceiver;
        [SerializeField] private RLPersonalizationAgent rlPersonalizationAgent;
        [SerializeField] private AudioRLAgent audioRLAgent;
        [SerializeField] private RLAdaptiveController rlAdaptiveController;
        [SerializeField] private LyriaPromptBuilder lyriaPromptBuilder;
        [SerializeField] private ActionSafetyShield actionSafetyShield;
        [SerializeField] private RuleBasedController ruleBasedFallbackController;
        [SerializeField] private AudioMixerController audioMixerController;
        [SerializeField] private SessionLogger sessionLogger;
        [SerializeField] private SafetyManager safetyManager;
        [SerializeField] private LyriaClipGenerationService clipGenerationService;
        [SerializeField] private ProductionSessionCoordinator productionCoordinator;
        [SerializeField] private ApplicationBootstrap visualBootstrap;
        [SerializeField] private bool excludeVisualChangesFromAudioLearning = true;

        [Header("Session Start Gate")]
        [SerializeField] private bool requireGeneratedMeditationBeforeSession = true;
        [SerializeField] private bool requireParticipantPreferences;

        [Header("Runtime State")]
        [SerializeField] private SessionState sessionState = new SessionState();

        public AudioProfile CurrentProfile => sessionState.activeProfile;
        public SignalPacket CurrentSignal => sessionState.latestSignal;
        public string CurrentSafetyMode => sessionState.safetyMode;
        public bool IsFallbackMode => sessionState.fallbackMode;
        public AudioParameters CurrentParameters => sessionState.latestParameters;
        public string CurrentStrategyName => sessionState.personalizationStrategy;
        public string CurrentActionName => sessionState.currentActionName;
        public string CurrentPolicyStatus => sessionState.policyStatus;
        public float CurrentReward => sessionState.latestReward;
        public AdaptiveControllerMode CurrentControllerMode => sessionState.controllerMode;
        public string CurrentPromptSummary => sessionState.currentLyriaFrame != null
            ? sessionState.currentLyriaFrame.promptSummary
            : CurrentProfile != null ? CurrentProfile.promptText : "Profile not generated yet.";
        public LyriaControlFrame CurrentLyriaFrame => sessionState.currentLyriaFrame;
        public AudioRLPolicyMode CurrentRLMode => audioRLAgent != null ? audioRLAgent.CurrentPolicyMode : AudioRLPolicyMode.RuleBasedAdaptive;
        public AudioRLAction CurrentProposedAction => audioRLAgent != null ? audioRLAgent.CurrentProposedAction : AudioRLAction.NoChange;
        public int CurrentOnlineUpdateCount => audioRLAgent != null ? audioRLAgent.OnlineUpdateCount : 0;
        internal AudioRLAgent MonitorAgent => audioRLAgent;
        public string CurrentLearningStatus => audioRLAgent != null ? audioRLAgent.CurrentLearningStatus : "No online agent";
        public AudioRLAction CurrentRuleAction => audioRLAgent != null ? audioRLAgent.CurrentRuleAction : AudioRLAction.NoChange;
        public AudioRLAction CurrentResidualAction => audioRLAgent != null ? audioRLAgent.CurrentResidualAction : AudioRLAction.NoChange;
        public AudioRLAction CurrentFinalSafeAction => audioRLAgent != null ? audioRLAgent.CurrentFinalSafeAction : AudioRLAction.NoChange;
        public string CurrentRLStateSummary => audioRLAgent != null ? audioRLAgent.CurrentStateSummary : "Legacy controller state";
        public string CurrentSafetyReason => audioRLAgent != null ? audioRLAgent.CurrentSafetyReason : sessionState.safetyMode;
        public int CurrentReplayBufferCount => audioRLAgent != null ? audioRLAgent.ReplayBufferCount : 0;
        public AudioRLRewardBreakdown CurrentRewardBreakdown => audioRLAgent != null ? audioRLAgent.CurrentRewardBreakdown : AudioRLRewardBreakdown.Zero;
        public bool IsPrepared { get; private set; }
        public bool IsSessionRunning { get; private set; }
        public string ParticipantPseudonym { get; private set; }
        public string RelaySessionId { get; private set; }
        public string CurrentSignalSource => componentBSignalReceiver != null
                                             && componentBSignalReceiver.HasLiveSignal
            ? "Component B live physiology"
            : "Signal simulator";

        private PersonalizationStrategy activeStrategy;
        private AudioClip observedMeditationClip;
        private object observedVisualState;
        private bool hasObservedVisualState;
        private long contentLease;
        private double contentMayFinishAt = double.PositiveInfinity;
        private bool realtimePlaybackReady = true;
        private SafetyManager subscribedSafetyManager;
        public AdaptationCoordinator SharedAdaptation => productionCoordinator != null ? productionCoordinator.SharedAdaptation : null;

        public bool TryBeginContentChange(string id, out long reservation)
        {
            reservation = 0;
            if (!IsSessionRunning || SharedAdaptation == null) return true;
            if (!SharedAdaptation.TryBeginContent(id, Time.realtimeSinceStartupAsDouble, 90, out reservation)) return false;
            contentLease = reservation;
            contentMayFinishAt = double.PositiveInfinity;
            return true;
        }

        public void CompleteContentChange(long reservation, float pipelineDelaySeconds = 0)
        {
            if (reservation != 0 && reservation == contentLease)
                contentMayFinishAt = Time.realtimeSinceStartupAsDouble + Mathf.Max(0, pipelineDelaySeconds);
        }

        public void InterruptAdaptation(string reason) => productionCoordinator?.InterruptCoordinatedAdaptation(reason);
        public void SetRealtimePlaybackReady(bool ready) => realtimePlaybackReady = ready;

        public void SetRuleOnlyMode()
        {
            audioRLAgent?.SetPolicyMode(AudioRLPolicyMode.RuleBasedAdaptive);
        }

        public void SetPpoResidualMode()
        {
            // Compatibility with previously serialized Unity button callbacks.
            SetPersonalizedBanditMode();
        }

        public void SetStaticPersonalizedMode() => audioRLAgent?.SetPolicyMode(AudioRLPolicyMode.StaticPersonalized);
        public void SetPersonalizedBanditMode() => audioRLAgent?.SetPolicyMode(AudioRLPolicyMode.PersonalizedBandit);

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable() => ResolveReferences();

        private void Start()
        {
            IsPrepared = false;
            IsSessionRunning = false;
            ResolveReferences();

            if (!ValidateReferences())
            {
                Debug.LogError("[PrototypeBootstrap] Startup aborted because required references are missing.", this);
                enabled = false;
                return;
            }

            if (requireParticipantPreferences) return;
            PrepareSession(preferenceManager.EnsureLoaded());
        }

        public bool TryConfigureParticipantSession(AudioSessionPreferences preferences)
        {
            if (preferences == null || preferences.Effective == null
                || string.IsNullOrWhiteSpace(preferences.SessionId)
                || string.IsNullOrWhiteSpace(preferences.ParticipantPseudonym)
                || preferences.Effective.userId != preferences.ParticipantPseudonym)
                return false;
            if (RelaySessionId == preferences.SessionId)
                return ParticipantPseudonym == preferences.ParticipantPseudonym;
            ResolveReferences();
            if (!ValidateReferences()) return false;

            FindAnyObjectByType<LyriaRealtimeStreamingService>()?.StopForParticipantChange();
            audioMixerController.HoldSessionPlayback();
            clipGenerationService?.ResetForParticipantSession();
            sessionLogger.EndSession();
            IsSessionRunning = false;
            IsPrepared = false;
            observedMeditationClip = null;
            observedVisualState = null;
            hasObservedVisualState = false;
            preferenceManager.SetParticipantPreferences(preferences.Effective);
            ParticipantPseudonym = preferences.ParticipantPseudonym;
            RelaySessionId = preferences.SessionId;
            PrepareSession(preferences.Effective);
            return IsPrepared;
        }

        private void PrepareSession(UserPreferences preferences)
        {
            AudioProfile profile = profileEngine.GenerateProfile(preferences);
            activeStrategy = rlPersonalizationAgent.SelectStrategy(preferences, profile, useLearning: false);

            sessionState.Reset(profile);
            sessionState.fallbackMode = preferenceManager.IsUsingFallback || profile == null;
            sessionState.personalizationStrategy = activeStrategy.displayName;

            if (audioRLAgent != null)
            {
                audioRLAgent.Initialize(profile, activeStrategy, RelaySessionId);
            }

            if (audioRLAgent == null && rlAdaptiveController != null)
            {
                rlAdaptiveController.Initialize(profile, activeStrategy);
            }
            if (ruleBasedFallbackController != null)
            {
                ruleBasedFallbackController.Initialize(profile);
            }

            sessionState.latestParameters = audioRLAgent != null
                ? audioRLAgent.CurrentParameters
                : rlAdaptiveController.CurrentParameters;
            sessionState.currentActionName = audioRLAgent != null
                ? audioRLAgent.CurrentActionName
                : rlAdaptiveController.CurrentActionName;
            sessionState.policyStatus = audioRLAgent != null
                ? audioRLAgent.CurrentPolicyStatus
                : rlAdaptiveController.CurrentPolicyStatus;
            sessionState.currentLyriaFrame = lyriaPromptBuilder.BuildFrame(
                profile,
                activeStrategy,
                sessionState.latestParameters,
                sessionState.controllerMode,
                SignalPacket.CreateDefault(),
                sessionState.currentActionName,
                sessionState.latestReward);

            audioMixerController.SetTargetParameters(sessionState.latestParameters);
            audioMixerController.HoldSessionPlayback();
            IsPrepared = true;

            if (!requireGeneratedMeditationBeforeSession)
            {
                TryBeginPreparedSession();
            }

            Debug.Log($"[PrototypeBootstrap] Session prepared for user {preferences.userId}.", this);
            Debug.Log($"[PrototypeBootstrap] Prompt profile: {profile.promptText}", this);
            Debug.Log($"[PrototypeBootstrap] RL personalization strategy: {activeStrategy.displayName}.", this);
            Debug.Log($"[PrototypeBootstrap] Fallback mode active: {sessionState.fallbackMode}", this);
        }

        private void Update()
        {
            UpdateSharedCoordination();
            if (IsSessionRunning && !string.IsNullOrWhiteSpace(RelaySessionId)
                && productionCoordinator != null && productionCoordinator.IsInitialized
                && (productionCoordinator.Phase == VrSessionPhase.Completed
                    || productionCoordinator.Phase == VrSessionPhase.Aborted))
            {
                audioRLAgent?.InvalidatePendingOutcome("Shared session ended");
                audioMixerController?.HoldSessionPlayback();
                sessionLogger?.EndSession();
                IsSessionRunning = false;
                IsPrepared = false;
                return;
            }

            if (IsPrepared && !IsSessionRunning && clipGenerationService != null
                && clipGenerationService.UsingGeneratedMeditationClip)
                TryBeginPreparedSession();

            if (!IsSessionRunning)
            {
                return;
            }

            if (signalSimulator == null || audioMixerController == null || sessionLogger == null || safetyManager == null)
            {
                return;
            }

            SignalPacket signal = signalSimulator.CurrentSignal;
            bool hasProfile = CurrentProfile != null;
            AudioParameters baseline = hasProfile
                ? (activeStrategy ?? PersonalizationStrategy.CreateNeutral()).ApplyTo(CurrentProfile.ToBaselineParameters())
                : default;

            sessionState.latestSignal = signal;
            sessionState.safetyMode = safetyManager.GetSafetyMode(hasProfile, signal);
            sessionState.fallbackMode = preferenceManager.IsUsingFallback || !hasProfile;

            AudioParameters nextParameters = baseline;
            sessionState.latestReward = 0f;

            if (hasProfile && audioRLAgent != null)
            {
                UpdateAudioLearningContext(signal);
                audioRLAgent.ObservePlayback(audioMixerController.CurrentAppliedParameters);
                nextParameters = audioRLAgent.Evaluate(signal, Time.deltaTime, safetyManager.IsSafeToRun());
                sessionState.controllerMode = audioRLAgent.CurrentControllerMode;
                sessionState.currentActionName = audioRLAgent.CurrentActionName;
                sessionState.policyStatus = audioRLAgent.CurrentPolicyStatus;
                sessionState.latestReward = audioRLAgent.CurrentReward;
                sessionState.safetyMode = audioRLAgent.CurrentSafetyMode.ToString();
                // The strategy initializes the profile; only the selected audio bandit
                // learns outcomes. Baseline study conditions never train another learner.
            }
            else if (safetyManager.IsSafeToRun() && sessionState.safetyMode == "Normal" && hasProfile && rlAdaptiveController != null)
            {
                nextParameters = rlAdaptiveController.Evaluate(signal, Time.deltaTime);
                sessionState.controllerMode = rlAdaptiveController.CurrentMode;
                sessionState.currentActionName = rlAdaptiveController.CurrentActionName;
                sessionState.policyStatus = rlAdaptiveController.CurrentPolicyStatus;
                sessionState.latestReward = rlAdaptiveController.CurrentReward;
                rlPersonalizationAgent.UpdateCurrentStrategy(sessionState.latestReward);
            }
            else if (hasProfile && ruleBasedFallbackController != null && sessionState.safetyMode != "EmergencyMuted")
            {
                nextParameters = ruleBasedFallbackController.Evaluate(signal, Time.deltaTime);
                sessionState.controllerMode = ruleBasedFallbackController.CurrentMode;
                sessionState.currentActionName = "Rule-Based Safety Fallback";
                sessionState.policyStatus = "Fallback";
            }
            else if (hasProfile)
            {
                nextParameters = baseline;
                sessionState.currentActionName = "Baseline Hold";
                sessionState.policyStatus = "Safety Hold";
            }

            if (audioRLAgent == null && actionSafetyShield != null)
            {
                bool usedSafetyFallback;
                nextParameters = actionSafetyShield.ClampParameters(
                    nextParameters,
                    audioMixerController.CurrentAppliedParameters,
                    baseline,
                    signal,
                    Time.deltaTime,
                    safetyManager.IsSafeToRun() && sessionState.safetyMode == "Normal",
                    out usedSafetyFallback);

                if (usedSafetyFallback)
                {
                    sessionState.fallbackMode = true;
                }
            }

            sessionState.latestParameters = nextParameters;
            sessionState.personalizationStrategy = activeStrategy != null ? activeStrategy.displayName : "Neutral Baseline";

            sessionState.currentLyriaFrame = lyriaPromptBuilder.BuildFrame(
                CurrentProfile,
                activeStrategy,
                nextParameters,
                sessionState.controllerMode,
                signal,
                sessionState.currentActionName,
                sessionState.latestReward);
            if (actionSafetyShield != null)
            {
                sessionState.currentLyriaFrame = actionSafetyShield.ClampFrame(sessionState.currentLyriaFrame, signal, nextParameters);
            }

            bool sharedSessionStopped = productionCoordinator != null && productionCoordinator.IsInitialized
                && (productionCoordinator.Phase == VrSessionPhase.Paused
                    || productionCoordinator.Phase == VrSessionPhase.Completed
                    || productionCoordinator.Phase == VrSessionPhase.Aborted);
            audioMixerController.SetMuted(safetyManager.EmergencyMute || sharedSessionStopped);
            audioMixerController.SetTargetParameters(nextParameters);
            if (audioRLAgent == null && rlAdaptiveController != null)
            {
                rlAdaptiveController.SyncAppliedParameters(nextParameters);
            }

            sessionLogger.LogFrame(
                signal,
                nextParameters,
                sessionState.controllerMode,
                sessionState.safetyMode,
                sessionState.fallbackMode,
                sessionState.personalizationStrategy,
                sessionState.policyStatus,
                sessionState.currentActionName,
                sessionState.latestReward,
                sessionState.currentLyriaFrame != null ? sessionState.currentLyriaFrame.config : default);
        }

        public bool TryBeginPreparedSession()
        {
            if (!IsPrepared || IsSessionRunning || audioMixerController == null || sessionLogger == null)
            {
                return false;
            }

            if (requireGeneratedMeditationBeforeSession
                && (clipGenerationService == null || !clipGenerationService.UsingGeneratedMeditationClip))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(RelaySessionId)
                && (productionCoordinator == null || !productionCoordinator.IsInitialized
                    || !productionCoordinator.IsNetworkConnected
                    || (productionCoordinator.Phase != VrSessionPhase.Acclimatization
                        && productionCoordinator.Phase != VrSessionPhase.Adaptive)))
                return false;

            if (!audioMixerController.BeginSessionPlayback())
            {
                return false;
            }

            sessionLogger.StartSession(RelaySessionId);
            IsSessionRunning = true;
            Debug.Log("[PrototypeBootstrap] Meditation session started with personalized generated audio.", this);
            return true;
        }

        private void UpdateAudioLearningContext(SignalPacket signal)
        {
            bool canAdapt = true;
            bool canObserve = true;
            string reason = "Audio adaptation active";
            if (productionCoordinator != null)
            {
                bool running = productionCoordinator.IsInitialized && productionCoordinator.IsNetworkConnected;
                canAdapt = running && productionCoordinator.Phase == VrSessionPhase.Adaptive && signal.hasPhysiologyWindow;
                canObserve = running && signal.hasPhysiologyWindow
                    && (productionCoordinator.Phase == VrSessionPhase.Acclimatization
                        || productionCoordinator.Phase == VrSessionPhase.Adaptive);
                reason = "Shared session: " + productionCoordinator.Phase;
            }
            AudioClip currentClip = audioMixerController.CurrentMeditationClip;
            if (observedMeditationClip != currentClip)
            {
                if (observedMeditationClip != null && SharedAdaptation != null
                    && SharedAdaptation.Status.Owner != AdaptationOwner.Content)
                    InterruptAdaptation("Unexpected meditation clip change");
                audioRLAgent.InvalidatePendingOutcome("Meditation clip changed");
                observedMeditationClip = currentClip;
            }
            if (audioMixerController.IsCrossfading)
            {
                canAdapt = canObserve = false;
                reason = "Meditation clip crossfade";
            }
            var visualManager = visualBootstrap != null ? visualBootstrap.EnvironmentManager : null;
            if (excludeVisualChangesFromAudioLearning && visualManager != null)
            {
                if (hasObservedVisualState && !visualManager.CurrentState.Equals(observedVisualState))
                {
                    if (SharedAdaptation != null && SharedAdaptation.Status.Owner != AdaptationOwner.Visual)
                        InterruptAdaptation("Unexpected visual environment change");
                    audioRLAgent.InvalidatePendingOutcome("Visual environment changed during audio attribution");
                }
                observedVisualState = visualManager.CurrentState;
                hasObservedVisualState = true;
                if (visualManager.IsTransitionActive)
                {
                    canAdapt = canObserve = false;
                    reason = "Visual transition; audio learning attribution suspended";
                }
            }
            audioRLAgent.SetControlContext(canAdapt, canObserve, reason);
        }

        private void UpdateSharedCoordination()
        {
            if (productionCoordinator == null || audioRLAgent == null) return;
            var coordination = productionCoordinator.EnableAudioCoordination();
            if (!ReferenceEquals(audioRLAgent.Coordination, coordination))
            {
                contentLease = 0;
                contentMayFinishAt = double.PositiveInfinity;
            }
            audioRLAgent.BindCoordination(coordination);
            if (contentLease != 0 && !coordination.IsCurrent(contentLease))
            {
                if (double.IsPositiveInfinity(contentMayFinishAt)) realtimePlaybackReady = false;
                contentLease = 0;
                contentMayFinishAt = double.PositiveInfinity;
            }
            if (contentLease != 0 && Time.realtimeSinceStartupAsDouble >= contentMayFinishAt
                && audioMixerController != null && !audioMixerController.IsCrossfading)
            {
                coordination.Release(contentLease, Time.realtimeSinceStartupAsDouble,
                    System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d, "Audio content change finished");
                contentLease = 0;
                observedMeditationClip = audioMixerController.CurrentMeditationClip;
            }
            bool expectedAudioTransition = coordination.Status.Owner == AdaptationOwner.Audio;
            bool parametersStable = audioMixerController != null && AudioActionCatalog.Same(
                AudioRLAction.Between(audioMixerController.CurrentAppliedParameters, audioRLAgent.CurrentTargetParameters),
                AudioRLAction.NoChange, 0.002f);
            productionCoordinator.SetCoordinatedAudioReady(IsSessionRunning && safetyManager != null
                && safetyManager.IsSafeToRun() && audioMixerController != null
                && audioMixerController.IsSessionPlaybackStarted && !audioMixerController.IsMeditationPlaybackPaused
                && realtimePlaybackReady
                && (!audioMixerController.IsCrossfading || coordination.Status.Owner == AdaptationOwner.Content)
                && (expectedAudioTransition || parametersStable));
        }

        private void OnDisable()
        {
            if (subscribedSafetyManager != null) subscribedSafetyManager.EmergencyMuteChanged -= HandleEmergencyMute;
            subscribedSafetyManager = null;
            productionCoordinator?.SetCoordinatedAudioReady(false);
            audioRLAgent?.InvalidatePendingOutcome("Audio session disabled");
        }

        private void ResolveReferences()
        {
            preferenceManager ??= FindAnyObjectByType<PreferenceManager>();
            profileEngine ??= FindAnyObjectByType<ProfileEngine>();
            signalSimulator ??= FindAnyObjectByType<SignalSimulator>();
            componentBSignalReceiver ??= FindAnyObjectByType<ComponentBStressSignalReceiver>();
            rlPersonalizationAgent ??= FindAnyObjectByType<RLPersonalizationAgent>();
            audioRLAgent ??= FindAnyObjectByType<AudioRLAgent>();
            productionCoordinator ??= FindAnyObjectByType<ProductionSessionCoordinator>();
            visualBootstrap ??= FindAnyObjectByType<ApplicationBootstrap>();
            rlAdaptiveController ??= FindAnyObjectByType<RLAdaptiveController>();
            lyriaPromptBuilder ??= FindAnyObjectByType<LyriaPromptBuilder>();
            actionSafetyShield ??= FindAnyObjectByType<ActionSafetyShield>();
            ruleBasedFallbackController ??= FindAnyObjectByType<RuleBasedController>();
            audioMixerController ??= FindAnyObjectByType<AudioMixerController>();
            sessionLogger ??= FindAnyObjectByType<SessionLogger>();
            safetyManager ??= FindAnyObjectByType<SafetyManager>();
            clipGenerationService ??= FindAnyObjectByType<LyriaClipGenerationService>();
            if (subscribedSafetyManager != safetyManager)
            {
                if (subscribedSafetyManager != null) subscribedSafetyManager.EmergencyMuteChanged -= HandleEmergencyMute;
                subscribedSafetyManager = safetyManager;
                if (subscribedSafetyManager != null) subscribedSafetyManager.EmergencyMuteChanged += HandleEmergencyMute;
            }
        }

        private void HandleEmergencyMute(bool muted)
        {
            if (!muted) return;
            productionCoordinator?.SetCoordinatedAudioReady(false);
            audioRLAgent?.InvalidatePendingOutcome("Emergency mute");
            audioMixerController?.SetMuted(true);
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(preferenceManager, nameof(preferenceManager));
            valid &= ReportMissing(profileEngine, nameof(profileEngine));
            valid &= ReportMissing(signalSimulator, nameof(signalSimulator));
            valid &= ReportMissing(rlPersonalizationAgent, nameof(rlPersonalizationAgent));
            valid &= ReportMissing(audioRLAgent, nameof(audioRLAgent));
            valid &= ReportMissing(lyriaPromptBuilder, nameof(lyriaPromptBuilder));
            valid &= ReportMissing(actionSafetyShield, nameof(actionSafetyShield));
            valid &= ReportMissing(audioMixerController, nameof(audioMixerController));
            valid &= ReportMissing(sessionLogger, nameof(sessionLogger));
            valid &= ReportMissing(safetyManager, nameof(safetyManager));
            if (requireGeneratedMeditationBeforeSession)
            {
                valid &= ReportMissing(clipGenerationService, nameof(clipGenerationService));
            }
            return valid;
        }

        private bool ReportMissing(Object reference, string label)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"[PrototypeBootstrap] Missing required reference: {label}", this);
            return false;
        }
    }
}
