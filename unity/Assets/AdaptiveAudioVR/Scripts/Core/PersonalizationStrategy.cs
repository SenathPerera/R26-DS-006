using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    [Serializable]
    public class PersonalizationStrategy
    {
        public string strategyId = "neutral_baseline";
        public string displayName = "Neutral Baseline";
        [TextArea(2, 5)] public string summary = "Balanced baseline strategy for meditation audio.";
        public string[] affinityTags = { "calm", "slow", "meditation" };
        public string[] accentPrompts = { "instrumental meditation", "soft evolving textures" };

        [Range(-0.35f, 0.35f)] public float intensityBias;
        [Range(-0.35f, 0.35f)] public float densityBias;
        [Range(-0.35f, 0.35f)] public float brightnessBias;
        [Range(-0.35f, 0.35f)] public float ambientMixBias;
        [Range(-0.35f, 0.35f)] public float musicMixBias;

        [Range(-20, 20)] public int bpmOffset;
        [Range(-1.5f, 1.5f)] public float guidanceBias;
        [Range(-0.8f, 0.8f)] public float temperatureBias;

        public bool muteBass;
        public bool muteDrums = true;

        public AudioParameters ApplyTo(AudioParameters baseline)
        {
            AudioParameters parameters = baseline;
            parameters.intensity += intensityBias;
            parameters.density += densityBias;
            parameters.brightness += brightnessBias;
            parameters.ambientMix += ambientMixBias;
            parameters.musicMix += musicMixBias;
            parameters = parameters.Clamp01();
            NormalizeMix(ref parameters);
            return parameters;
        }

        public static PersonalizationStrategy CreateNeutral()
        {
            return new PersonalizationStrategy();
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
