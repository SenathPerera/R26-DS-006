using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Controller
{
    public class RuleBasedController : MonoBehaviour
    {
        [Header("Thresholds")]
        [SerializeField] private float lowConfidenceThreshold = 0.45f;
        [SerializeField] private float highStressThreshold = 0.65f;
        [SerializeField] private float lowStressThreshold = 0.35f;

        [Header("Adaptation")]
        [SerializeField] private float maxDeltaPerSecond = 0.35f;
        [SerializeField] private float lowConfidenceSlowdownMultiplier = 0.35f;
        [SerializeField] private float baselineDriftStrength = 0.65f;

        [Header("Stress Response")]
        [SerializeField] private float stressResponseStrength = 0.22f;
        [SerializeField] private float calmResponseStrength = 0.18f;

        public AudioProfile ActiveProfile { get; private set; }
        public AudioParameters CurrentParameters { get; private set; }
        public AdaptiveControllerMode CurrentMode { get; private set; } = AdaptiveControllerMode.Initialized;
        public bool IsInitialized => ActiveProfile != null;

        public void Initialize(AudioProfile profile)
        {
            ActiveProfile = profile;
            CurrentParameters = profile != null ? profile.ToBaselineParameters() : default;
            CurrentMode = AdaptiveControllerMode.Initialized;
        }

        public AudioParameters Evaluate(SignalPacket signal, float deltaTime)
        {
            if (ActiveProfile == null)
            {
                CurrentMode = AdaptiveControllerMode.Initialized;
                return CurrentParameters;
            }

            var baseline = ActiveProfile.ToBaselineParameters();
            var target = baseline;
            float confidence = Mathf.Clamp01(signal.confidence);
            float stress = Mathf.Clamp01(signal.stress);

            if (confidence < lowConfidenceThreshold)
            {
                CurrentMode = AdaptiveControllerMode.LowConfidenceDampened;
                float baselineBias = Mathf.Lerp(baselineDriftStrength, 1f, confidence / Mathf.Max(0.01f, lowConfidenceThreshold));
                target = AudioParameters.Lerp(CurrentParameters, baseline, baselineBias);
                CurrentParameters = AudioParameters.MoveTowards(CurrentParameters, target, maxDeltaPerSecond * lowConfidenceSlowdownMultiplier * deltaTime);
                return CurrentParameters.Clamp01();
            }

            if (stress >= highStressThreshold)
            {
                CurrentMode = AdaptiveControllerMode.HighStressAdaptive;
                float factor = Mathf.InverseLerp(highStressThreshold, 1f, stress);
                target.intensity = baseline.intensity + (stressResponseStrength * factor);
                target.density = baseline.density + ((stressResponseStrength - 0.02f) * factor);
                target.brightness = baseline.brightness + ((stressResponseStrength - 0.04f) * factor);
                target.musicMix = baseline.musicMix + (0.18f * factor);
                target.ambientMix = baseline.ambientMix - (0.18f * factor);
            }
            else if (stress <= lowStressThreshold)
            {
                CurrentMode = AdaptiveControllerMode.LowStressCalming;
                float factor = Mathf.InverseLerp(lowStressThreshold, 0f, stress);
                target.intensity = baseline.intensity - (calmResponseStrength * factor);
                target.density = baseline.density - ((calmResponseStrength - 0.03f) * factor);
                target.brightness = baseline.brightness - ((calmResponseStrength - 0.06f) * factor);
                target.musicMix = baseline.musicMix - (0.14f * factor);
                target.ambientMix = baseline.ambientMix + (0.14f * factor);
            }
            else
            {
                CurrentMode = AdaptiveControllerMode.MidRangeStabilizing;
                target = baseline;
            }

            target = target.Clamp01();
            NormalizeMix(ref target);
            CurrentParameters = AudioParameters.MoveTowards(CurrentParameters, target, maxDeltaPerSecond * deltaTime);
            return CurrentParameters.Clamp01();
        }

        private static void NormalizeMix(ref AudioParameters parameters)
        {
            float total = parameters.ambientMix + parameters.musicMix;
            if (total <= 0.001f)
            {
                parameters.ambientMix = 0.5f;
                parameters.musicMix = 0.5f;
                return;
            }

            parameters.ambientMix /= total;
            parameters.musicMix /= total;
        }
    }
}
