using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    public sealed class AudioRuleBaselinePolicy
    {
        private readonly float lowConfidenceThreshold;
        private readonly float highStressThreshold;
        private readonly float lowStressThreshold;

        public AudioRuleBaselinePolicy(float lowConfidenceThreshold, float highStressThreshold, float lowStressThreshold)
        {
            this.lowConfidenceThreshold = lowConfidenceThreshold;
            this.highStressThreshold = highStressThreshold;
            this.lowStressThreshold = lowStressThreshold;
        }

        public AudioRLAction GetAction(AudioRLState state, float maximumDelta)
        {
            AudioParameters current = state.currentParameters;
            AudioParameters target = state.personalizedBaseline;
            float confidence = Mathf.Clamp01(state.signal.confidence);
            float stress = Mathf.Clamp01(state.signal.stress);

            if (confidence < lowConfidenceThreshold)
            {
                target = AudioParameters.Lerp(current, target, 0.55f);
            }
            else if (stress > highStressThreshold)
            {
                target.intensity += 0.10f;
                target.density += 0.08f;
                target.brightness += 0.05f;
                target.tempo -= 0.03f;
                target.fade += 0.05f;
                target.musicMix += 0.10f;
                target.ambientMix -= 0.10f;
            }
            else if (stress < lowStressThreshold)
            {
                target.intensity -= 0.08f;
                target.density -= 0.06f;
                target.brightness -= 0.04f;
                target.tempo -= 0.04f;
                target.fade += 0.04f;
                target.musicMix -= 0.08f;
                target.ambientMix += 0.08f;
            }

            target = target.Clamp01();
            target.NormalizeMix();
            return AudioRLAction.Between(current, target).Clamp(maximumDelta);
        }
    }
}
