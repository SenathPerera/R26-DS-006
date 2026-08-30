using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Safety
{
    public class ActionSafetyShield : MonoBehaviour
    {
        [SerializeField] private float maxParameterDeltaPerSecond = 0.22f;
        [SerializeField] private float lowConfidenceThreshold = 0.45f;
        [SerializeField] private float lowConfidenceActionScale = 0.35f;
        [SerializeField] private float lowConfidenceBaselineBlend = 0.45f;
        [SerializeField] private float maxAllowedBrightness = 0.90f;
        [SerializeField] private float minAllowedAmbientMix = 0.15f;
        [SerializeField] private float maxAllowedMusicMix = 0.85f;

        public AudioParameters ClampParameters(
            AudioParameters candidate,
            AudioParameters current,
            AudioParameters baseline,
            SignalPacket signal,
            float deltaTime,
            bool safeToRun,
            out bool usedFallback)
        {
            usedFallback = false;

            if (!safeToRun)
            {
                usedFallback = true;
                return baseline.Clamp01();
            }

            float deltaScale = Mathf.Lerp(lowConfidenceActionScale, 1f, signal.confidence);
            AudioParameters safe = AudioParameters.MoveTowards(current, candidate.Clamp01(), maxParameterDeltaPerSecond * deltaScale * Mathf.Max(deltaTime, 0.0001f));

            safe.brightness = Mathf.Clamp(safe.brightness, 0f, maxAllowedBrightness);
            safe.ambientMix = Mathf.Clamp(safe.ambientMix, minAllowedAmbientMix, 1f);
            safe.musicMix = Mathf.Clamp(safe.musicMix, 0f, maxAllowedMusicMix);
            NormalizeMix(ref safe);

            if (signal.confidence < lowConfidenceThreshold)
            {
                safe = AudioParameters.Lerp(safe, baseline, lowConfidenceBaselineBlend);
                usedFallback = true;
            }

            return safe.Clamp01();
        }

        public LyriaControlFrame ClampFrame(LyriaControlFrame frame, SignalPacket signal, AudioParameters safeParameters)
        {
            if (frame == null)
            {
                return null;
            }

            frame.Normalize();
            LyriaGenerationConfig config = frame.config;
            config.density = safeParameters.density;
            config.brightness = safeParameters.brightness;

            if (signal.confidence < lowConfidenceThreshold)
            {
                config.guidance = Mathf.Lerp(config.guidance, 3.5f, lowConfidenceBaselineBlend);
                config.temperature = Mathf.Lerp(config.temperature, 0.85f, lowConfidenceBaselineBlend);
            }

            frame.config = config.Normalize();
            return frame;
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
