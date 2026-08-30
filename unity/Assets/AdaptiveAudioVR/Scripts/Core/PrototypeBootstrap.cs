using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Controller;
using AdaptiveAudioVR.Logging;
using AdaptiveAudioVR.Preference;
using AdaptiveAudioVR.Profile;
using AdaptiveAudioVR.RL;
using AdaptiveAudioVR.Safety;
using AdaptiveAudioVR.Signals;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    public class PrototypeBootstrap : MonoBehaviour
    {
        [Header("Subsystem References")]
        [SerializeField] private PreferenceManager preferenceManager;
        [SerializeField] private ProfileEngine profileEngine;
        [SerializeField] private SignalSimulator signalSimulator;
        [SerializeField] private RLPersonalizationAgent rlPersonalizationAgent;
        [SerializeField] private RLAdaptiveController rlAdaptiveController;
        [SerializeField] private LyriaPromptBuilder lyriaPromptBuilder;
        [SerializeField] private ActionSafetyShield actionSafetyShield;
        [SerializeField] private RuleBasedController ruleBasedFallbackController;
        [SerializeField] private AudioMixerController audioMixerController;
        [SerializeField] private SessionLogger sessionLogger;
        [SerializeField] private SafetyManager safetyManager;

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

        private PersonalizationStrategy activeStrategy;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();

            if (!ValidateReferences())
            {
                Debug.LogError("[PrototypeBootstrap] Startup aborted because required references are missing.", this);
                enabled = false;
                return;
            }

            UserPreferences preferences = preferenceManager.EnsureLoaded();
            AudioProfile profile = profileEngine.GenerateProfile(preferences);
            activeStrategy = rlPersonalizationAgent.SelectStrategy(preferences, profile);

            sessionState.Reset(profile);
            sessionState.fallbackMode = preferenceManager.IsUsingFallback || profile == null;
            sessionState.personalizationStrategy = activeStrategy.displayName;

            rlAdaptiveController.Initialize(profile, activeStrategy);
            if (ruleBasedFallbackController != null)
            {
                ruleBasedFallbackController.Initialize(profile);
            }

            sessionState.latestParameters = rlAdaptiveController.CurrentParameters;
            sessionState.currentActionName = rlAdaptiveController.CurrentActionName;
            sessionState.policyStatus = rlAdaptiveController.CurrentPolicyStatus;
            sessionState.currentLyriaFrame = lyriaPromptBuilder.BuildFrame(
                profile,
                activeStrategy,
                sessionState.latestParameters,
                sessionState.controllerMode,
                SignalPacket.CreateDefault(),
                sessionState.currentActionName,
                sessionState.latestReward);

            audioMixerController.SetTargetParameters(sessionState.latestParameters);
            sessionLogger.StartSession();

            Debug.Log($"[PrototypeBootstrap] Prototype initialized for user {preferences.userId}.", this);
            Debug.Log($"[PrototypeBootstrap] Prompt profile: {profile.promptText}", this);
            Debug.Log($"[PrototypeBootstrap] RL personalization strategy: {activeStrategy.displayName}.", this);
            Debug.Log($"[PrototypeBootstrap] Fallback mode active: {sessionState.fallbackMode}", this);
        }

        private void Update()
        {
            if (signalSimulator == null || rlAdaptiveController == null || audioMixerController == null || sessionLogger == null || safetyManager == null)
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
            sessionState.fallbackMode = preferenceManager.IsUsingFallback
                                        || sessionState.safetyMode == "MissingProfileFallback"
                                        || sessionState.safetyMode == "SignalTimeoutFallback";

            AudioParameters nextParameters = baseline;
            sessionState.latestReward = 0f;

            if (safetyManager.IsSafeToRun() && sessionState.safetyMode == "Normal" && hasProfile)
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
            sessionState.currentLyriaFrame = actionSafetyShield.ClampFrame(sessionState.currentLyriaFrame, signal, nextParameters);

            audioMixerController.SetMuted(safetyManager.EmergencyMute);
            audioMixerController.SetTargetParameters(nextParameters);
            rlAdaptiveController.SyncAppliedParameters(nextParameters);

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

        private void ResolveReferences()
        {
            preferenceManager ??= FindAnyObjectByType<PreferenceManager>();
            profileEngine ??= FindAnyObjectByType<ProfileEngine>();
            signalSimulator ??= FindAnyObjectByType<SignalSimulator>();
            rlPersonalizationAgent ??= FindAnyObjectByType<RLPersonalizationAgent>();
            rlAdaptiveController ??= FindAnyObjectByType<RLAdaptiveController>();
            lyriaPromptBuilder ??= FindAnyObjectByType<LyriaPromptBuilder>();
            actionSafetyShield ??= FindAnyObjectByType<ActionSafetyShield>();
            ruleBasedFallbackController ??= FindAnyObjectByType<RuleBasedController>();
            audioMixerController ??= FindAnyObjectByType<AudioMixerController>();
            sessionLogger ??= FindAnyObjectByType<SessionLogger>();
            safetyManager ??= FindAnyObjectByType<SafetyManager>();
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(preferenceManager, nameof(preferenceManager));
            valid &= ReportMissing(profileEngine, nameof(profileEngine));
            valid &= ReportMissing(signalSimulator, nameof(signalSimulator));
            valid &= ReportMissing(rlPersonalizationAgent, nameof(rlPersonalizationAgent));
            valid &= ReportMissing(rlAdaptiveController, nameof(rlAdaptiveController));
            valid &= ReportMissing(lyriaPromptBuilder, nameof(lyriaPromptBuilder));
            valid &= ReportMissing(actionSafetyShield, nameof(actionSafetyShield));
            valid &= ReportMissing(audioMixerController, nameof(audioMixerController));
            valid &= ReportMissing(sessionLogger, nameof(sessionLogger));
            valid &= ReportMissing(safetyManager, nameof(safetyManager));
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
