using System.Reflection;
using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Controller;
using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.Logging;
using AdaptiveAudioVR.Preference;
using AdaptiveAudioVR.Profile;
using AdaptiveAudioVR.RL;
using AdaptiveAudioVR.RL.Agent;
using AdaptiveAudioVR.Safety;
using AdaptiveAudioVR.Signals;
using AdaptiveAudioVR.UI;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdaptiveAudioVR.Integration
{
    [DisallowMultipleComponent]
    public class AdaptiveAudioVrSceneInstaller : MonoBehaviour
    {
        [Header("VR Environment")]
        [SerializeField] private string environmentId = "japanese_temple_pond_garden";
        [SerializeField] private string environmentDisplayName = "Japanese Temple Pond Garden";
        [SerializeField, TextArea(2, 5)] private string meditationMusicReference =
            "serene Japanese temple garden musical character with breathy bamboo flute, soft plucked strings, sparse temple bells, spacious stillness; music only, no pond, wind, bird, rain, or other environmental sound effects";

        [Header("Scene Audio Clips")]
        [SerializeField] private AudioClip fixedAmbientClip;
        [SerializeField] private AudioClip fallbackMeditationClip;

        [Header("Backend")]
        [SerializeField] private string backendBaseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string realtimeWebsocketBaseUrl = "ws://127.0.0.1:8000/live-music";

        [Header("Runtime Options")]
        [SerializeField] private bool installOnAwake = true;
        [SerializeField] private bool includeValidationDashboard = true;
        [SerializeField] private bool includeRealtimeBridge = true;
        [SerializeField] private bool autoGenerateMeditationClipOnStart = false;
        [SerializeField] private bool autoGenerateOnActionChange = false;
        [SerializeField] private bool autoCheckRealtimeCapabilityOnStart = true;
        [SerializeField] private bool requireGeneratedMeditationBeforeSession = true;

        [Header("StreamingAssets Files")]
        [SerializeField] private string preferenceConfigFileName = "sample_user_preferences.json";
        [SerializeField] private string directPpoPolicyFileName = "ppo_seed_37_unity_network.json";
        [SerializeField] private string importedPolicyFileName = "ppo_seed_37_unity_policy.json";

        private const string AppRootName = "AppRoot";
        private const string SignalSystemName = "SignalSystem";
        private const string ControllerSystemName = "ControllerSystem";
        private const string AudioSystemName = "AudioSystem";
        private const string UiSystemName = "UISystem";
        private const string MeditationPlayerName = "MeditationPlayer";
        private const string AmbientPlayerName = "AmbientPlayer";

        private void Awake()
        {
            if (installOnAwake)
            {
                InstallAdaptiveAudioRuntime();
            }
        }

        [ContextMenu("Install Adaptive Audio Runtime")]
        public void InstallAdaptiveAudioRuntime()
        {
            GameObject appRoot = GetOrCreateNamedObject(AppRootName);
            GameObject signalSystem = GetOrCreateNamedObject(SignalSystemName);
            GameObject controllerSystem = GetOrCreateNamedObject(ControllerSystemName);
            GameObject audioSystem = GetOrCreateNamedObject(AudioSystemName);
            GameObject uiSystem = includeValidationDashboard ? GetOrCreateNamedObject(UiSystemName) : GameObject.Find(UiSystemName);

            AudioSource meditationSource = GetOrCreateAudioSource(MeditationPlayerName);
            AudioSource ambientSource = GetOrCreateAudioSource(AmbientPlayerName);

            AudioLowPassFilter meditationLowPass = GetOrAddComponent<AudioLowPassFilter>(meditationSource.gameObject);
            AudioReverbFilter meditationReverb = GetOrAddComponent<AudioReverbFilter>(meditationSource.gameObject);
            ConfigureAudioSource(meditationSource, fallbackMeditationClip, priority: 32);
            ConfigureAudioSource(ambientSource, fixedAmbientClip, priority: 64);

            PreferenceManager preferenceManager = GetOrAddComponent<PreferenceManager>(appRoot);
            ProfileEngine profileEngine = GetOrAddComponent<ProfileEngine>(appRoot);
            PrototypeBootstrap bootstrap = GetOrAddComponent<PrototypeBootstrap>(appRoot);

            SignalSimulator signalSimulator = GetOrAddComponent<SignalSimulator>(signalSystem);
            ComponentBStressSignalReceiver componentBSignalReceiver =
                GetOrAddComponent<ComponentBStressSignalReceiver>(signalSystem);

            RLPersonalizationAgent rlPersonalizationAgent = GetOrAddComponent<RLPersonalizationAgent>(controllerSystem);
            AudioRLAgent audioRLAgent = GetOrAddComponent<AudioRLAgent>(controllerSystem);
            AudioRLTransitionLogger transitionLogger = GetOrAddComponent<AudioRLTransitionLogger>(controllerSystem);
            RLAdaptiveController rlAdaptiveController = GetOrAddComponent<RLAdaptiveController>(controllerSystem);
            LyriaPromptBuilder lyriaPromptBuilder = GetOrAddComponent<LyriaPromptBuilder>(controllerSystem);
            ActionSafetyShield actionSafetyShield = GetOrAddComponent<ActionSafetyShield>(controllerSystem);
            RuleBasedController ruleBasedController = GetOrAddComponent<RuleBasedController>(controllerSystem);
            SafetyManager safetyManager = GetOrAddComponent<SafetyManager>(controllerSystem);

            AudioMixerController audioMixerController = GetOrAddComponent<AudioMixerController>(audioSystem);
            SessionLogger sessionLogger = GetOrAddComponent<SessionLogger>(audioSystem);
            LyriaClipGenerationService clipGenerationService = GetOrAddComponent<LyriaClipGenerationService>(audioSystem);
            LyriaRealtimeStreamingService realtimeStreamingService = includeRealtimeBridge
                ? GetOrAddComponent<LyriaRealtimeStreamingService>(audioSystem)
                : audioSystem.GetComponent<LyriaRealtimeStreamingService>();

            RealtimePcmAudioPlayer pcmAudioPlayer = includeRealtimeBridge
                ? GetOrAddComponent<RealtimePcmAudioPlayer>(audioSystem)
                : audioSystem.GetComponent<RealtimePcmAudioPlayer>();

            AdaptiveDashboardInstaller dashboardInstaller = includeValidationDashboard
                ? GetOrAddComponent<AdaptiveDashboardInstaller>(uiSystem)
                : (uiSystem != null ? uiSystem.GetComponent<AdaptiveDashboardInstaller>() : null);

            SetPrivateField(preferenceManager, "configFileName", preferenceConfigFileName);

            SetPrivateField(rlAdaptiveController, "importedPolicyFileName", importedPolicyFileName);
            SetPrivateField(audioRLAgent, "directPpoPolicyFileName", directPpoPolicyFileName);
            SetPrivateField(audioRLAgent, "importedPolicyFileName", importedPolicyFileName);
            SetPrivateField(audioRLAgent, "transitionLogger", transitionLogger);

            lyriaPromptBuilder.ConfigureEnvironment(
                ResolveEnvironmentId(),
                ResolveEnvironmentDisplayName(),
                meditationMusicReference);

            SetPrivateField(audioMixerController, "meditationSource", meditationSource);
            SetPrivateField(audioMixerController, "ambientSource", ambientSource);
            SetPrivateField(audioMixerController, "meditationLowPass", meditationLowPass);
            SetPrivateField(audioMixerController, "meditationReverb", meditationReverb);
            SetPrivateField(audioMixerController, "requireExplicitSessionStart", requireGeneratedMeditationBeforeSession);

            SetPrivateField(clipGenerationService, "bootstrap", bootstrap);
            SetPrivateField(clipGenerationService, "audioMixerController", audioMixerController);
            SetPrivateField(clipGenerationService, "backendBaseUrl", backendBaseUrl);
            SetPrivateField(clipGenerationService, "generateOnStart", autoGenerateMeditationClipOnStart);
            SetPrivateField(clipGenerationService, "autoGenerateOnActionChange", autoGenerateOnActionChange);
            SetPrivateField(clipGenerationService, "requireGeneratedClipBeforeSession", requireGeneratedMeditationBeforeSession);
            SetPrivateField(clipGenerationService, "requiredEnvironmentId", ResolveEnvironmentId());

            if (realtimeStreamingService != null)
            {
                SetPrivateField(realtimeStreamingService, "bootstrap", bootstrap);
                SetPrivateField(realtimeStreamingService, "audioMixerController", audioMixerController);
                SetPrivateField(realtimeStreamingService, "clipGenerationService", clipGenerationService);
                SetPrivateField(realtimeStreamingService, "pcmAudioPlayer", pcmAudioPlayer);
                SetPrivateField(realtimeStreamingService, "websocketBaseUrl", realtimeWebsocketBaseUrl);
                SetPrivateField(realtimeStreamingService, "autoCheckCapabilityOnStart", autoCheckRealtimeCapabilityOnStart);
            }

            if (dashboardInstaller != null)
            {
                SetPrivateField(dashboardInstaller, "bootstrap", bootstrap);
                SetPrivateField(dashboardInstaller, "signalSimulator", signalSimulator);
                SetPrivateField(dashboardInstaller, "safetyManager", safetyManager);
                SetPrivateField(dashboardInstaller, "lyriaClipGenerationService", clipGenerationService);
                SetPrivateField(dashboardInstaller, "lyriaRealtimeStreamingService", realtimeStreamingService);
                SetPrivateField(dashboardInstaller, "audioMixerController", audioMixerController);
                SetPrivateField(dashboardInstaller, "meditationSource", meditationSource);
                SetPrivateField(dashboardInstaller, "ambientSource", ambientSource);
            }

            SetPrivateField(bootstrap, "preferenceManager", preferenceManager);
            SetPrivateField(bootstrap, "profileEngine", profileEngine);
            SetPrivateField(bootstrap, "signalSimulator", signalSimulator);
            SetPrivateField(bootstrap, "rlPersonalizationAgent", rlPersonalizationAgent);
            SetPrivateField(bootstrap, "audioRLAgent", audioRLAgent);
            SetPrivateField(bootstrap, "rlAdaptiveController", rlAdaptiveController);
            SetPrivateField(bootstrap, "lyriaPromptBuilder", lyriaPromptBuilder);
            SetPrivateField(bootstrap, "actionSafetyShield", actionSafetyShield);
            SetPrivateField(bootstrap, "ruleBasedFallbackController", ruleBasedController);
            SetPrivateField(bootstrap, "audioMixerController", audioMixerController);
            SetPrivateField(bootstrap, "sessionLogger", sessionLogger);
            SetPrivateField(bootstrap, "safetyManager", safetyManager);
            SetPrivateField(bootstrap, "clipGenerationService", clipGenerationService);
            SetPrivateField(bootstrap, "requireGeneratedMeditationBeforeSession", requireGeneratedMeditationBeforeSession);

            ComponentBPhysiologyBridge componentBBridge =
                FindAnyObjectByType<ComponentBPhysiologyBridge>();
            componentBSignalReceiver.Configure(
                componentBBridge,
                signalSimulator);

            if (componentBBridge == null)
            {
                Debug.LogWarning(
                    "[AdaptiveAudioVrSceneInstaller] Component B bridge was not found; "
                    + "audio will keep using its configured simulator until one is assigned.",
                    this);
            }

            Debug.Log("[AdaptiveAudioVrSceneInstaller] Adaptive audio runtime is installed in the current VR scene.", this);
        }

        private string ResolveEnvironmentId()
        {
            if (!string.IsNullOrWhiteSpace(environmentId))
            {
                return environmentId.Trim();
            }

            return SceneManager.GetActiveScene().name.Trim().ToLowerInvariant().Replace(' ', '_');
        }

        private string ResolveEnvironmentDisplayName()
        {
            return string.IsNullOrWhiteSpace(environmentDisplayName)
                ? SceneManager.GetActiveScene().name
                : environmentDisplayName.Trim();
        }

        private static AudioSource GetOrCreateAudioSource(string objectName)
        {
            GameObject audioObject = GetOrCreateNamedObject(objectName);
            return GetOrAddComponent<AudioSource>(audioObject);
        }

        private static GameObject GetOrCreateNamedObject(string objectName)
        {
            GameObject existing = GameObject.Find(objectName);
            if (existing != null)
            {
                return existing;
            }

            return new GameObject(objectName);
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            if (target.TryGetComponent(out T existing))
            {
                return existing;
            }

            return target.AddComponent<T>();
        }

        private static void ConfigureAudioSource(AudioSource source, AudioClip clip, int priority)
        {
            if (source == null)
            {
                return;
            }

            if (clip != null)
            {
                source.clip = clip;
            }

            source.playOnAwake = false;
            source.loop = true;
            source.priority = priority;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        private static void SetPrivateField(Component target, string fieldName, object value)
        {
            if (target == null || value == null)
            {
                return;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return;
            }

            field.SetValue(target, value);
        }
    }
}
