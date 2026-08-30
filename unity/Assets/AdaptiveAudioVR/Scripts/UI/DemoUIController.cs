using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.Integration;
using AdaptiveAudioVR.RL;
using AdaptiveAudioVR.Safety;
using AdaptiveAudioVR.Signals;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveAudioVR.UI
{
    public class DemoUIController : MonoBehaviour
    {
        [Header("System References")]
        [SerializeField] private SignalSimulator signalSimulator = null;
        [SerializeField] private RLAdaptiveController rlAdaptiveController = null;
        [SerializeField] private SafetyManager safetyManager = null;
        [SerializeField] private PrototypeBootstrap bootstrap = null;
        [SerializeField] private LyriaClipGenerationService lyriaClipGenerationService = null;

        [Header("Controls")]
        [SerializeField] private Slider stressSlider = null;
        [SerializeField] private Slider confidenceSlider = null;
        [SerializeField] private Toggle emergencyMuteToggle = null;

        [Header("Labels")]
        [SerializeField] private Text stressValueText = null;
        [SerializeField] private Text confidenceValueText = null;
        [SerializeField] private Text controllerModeText = null;
        [SerializeField] private Text safetyModeText = null;
        [SerializeField] private Text currentProfilePromptText = null;
        [SerializeField] private Text currentAudioParametersText = null;
        [SerializeField] private Text lyriaStatusText = null;
        [SerializeField] private Text lyriaClipText = null;

        private void Start()
        {
            if (stressSlider != null)
            {
                stressSlider.onValueChanged.AddListener(OnStressSliderChanged);
            }

            if (confidenceSlider != null)
            {
                confidenceSlider.onValueChanged.AddListener(OnConfidenceSliderChanged);
            }

            if (emergencyMuteToggle != null)
            {
                emergencyMuteToggle.onValueChanged.AddListener(OnEmergencyMuteChanged);
            }

            RefreshStaticText();
        }

        private void Update()
        {
            RefreshDynamicText();
        }

        public void SetModeManual()
        {
            signalSimulator?.SetModeManual();
        }

        public void SetModeOscillation()
        {
            signalSimulator?.SetModeOscillation();
        }

        public void SetModeRandomWalk()
        {
            signalSimulator?.SetModeRandomWalk();
        }

        public void ToggleEmergencyMute()
        {
            if (safetyManager == null)
            {
                return;
            }

            safetyManager.EmergencyMute = !safetyManager.EmergencyMute;
            if (emergencyMuteToggle != null)
            {
                emergencyMuteToggle.isOn = safetyManager.EmergencyMute;
            }
        }

        public void GenerateLyriaClip()
        {
            lyriaClipGenerationService?.RequestGenerationFromCurrentPrompt();
        }

        public void RestoreRawMeditationClip()
        {
            lyriaClipGenerationService?.RestoreOriginalMeditationClip();
        }

        public void RefreshLyriaBackend()
        {
            lyriaClipGenerationService?.RequestBackendHealthRefresh();
        }

        private void OnStressSliderChanged(float value)
        {
            signalSimulator?.SetManualStress(value);
        }

        private void OnConfidenceSliderChanged(float value)
        {
            signalSimulator?.SetManualConfidence(value);
        }

        private void OnEmergencyMuteChanged(bool value)
        {
            if (safetyManager != null)
            {
                safetyManager.EmergencyMute = value;
            }
        }

        private void RefreshStaticText()
        {
            if (currentProfilePromptText == null)
            {
                return;
            }

            currentProfilePromptText.text = bootstrap != null ? bootstrap.CurrentPromptSummary : "Profile not generated yet.";
        }

        private void RefreshDynamicText()
        {
            SignalPacket signal = signalSimulator != null ? signalSimulator.CurrentSignal : SignalPacket.CreateDefault();
            AudioParameters parameters = bootstrap != null ? bootstrap.CurrentParameters : default;

            if (stressValueText != null)
            {
                stressValueText.text = $"Stress: {signal.stress:F2}";
            }

            if (confidenceValueText != null)
            {
                confidenceValueText.text = $"Confidence: {signal.confidence:F2}";
            }

            if (controllerModeText != null)
            {
                if (bootstrap != null)
                {
                    controllerModeText.text = $"Controller: {bootstrap.CurrentControllerMode} | {bootstrap.CurrentPolicyStatus} | {bootstrap.CurrentActionName}";
                }
                else
                {
                    controllerModeText.text = $"Controller: {(rlAdaptiveController != null ? rlAdaptiveController.CurrentMode.ToString() : "Unavailable")}";
                }
            }

            if (safetyModeText != null)
            {
                string safetyMode = bootstrap != null ? bootstrap.CurrentSafetyMode : "Unknown";
                safetyModeText.text = $"Safety: {safetyMode}";
            }

            if (currentProfilePromptText != null)
            {
                currentProfilePromptText.text = bootstrap != null ? bootstrap.CurrentPromptSummary : "Profile not generated yet.";
            }

            if (currentAudioParametersText != null)
            {
                string rewardSuffix = bootstrap != null ? $" | Reward {bootstrap.CurrentReward:F2} | {bootstrap.CurrentStrategyName}" : string.Empty;
                currentAudioParametersText.text = parameters + rewardSuffix;
            }

            if (lyriaStatusText != null && lyriaClipGenerationService != null)
            {
                lyriaStatusText.text =
                    $"Lyria: {lyriaClipGenerationService.LastBackendHealthSummary} | {lyriaClipGenerationService.LastStatusMessage}";
            }

            if (lyriaClipText != null && lyriaClipGenerationService != null)
            {
                string clipName = string.IsNullOrWhiteSpace(lyriaClipGenerationService.LastGeneratedClipPath)
                    ? "none"
                    : System.IO.Path.GetFileName(lyriaClipGenerationService.LastGeneratedClipPath);
                lyriaClipText.text =
                    $"Playback: {lyriaClipGenerationService.CurrentPlaybackSourceLabel} | Clip: {clipName}";
            }

            if (stressSlider != null && !stressSlider.interactable)
            {
                stressSlider.interactable = true;
            }

            if (confidenceSlider != null && !confidenceSlider.interactable)
            {
                confidenceSlider.interactable = true;
            }
        }
    }
}
