using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    [Serializable]
    public class AudioProfile
    {
        public string userId;
        public string mood;
        public string ambience;
        public string tempo;
        public string[] instruments;
        public bool avoidDissonance;
        [Range(0f, 1f)] public float noveltyTolerance;

        [Range(0f, 1f)] public float baseIntensity;
        [Range(0f, 1f)] public float baseDensity;
        [Range(0f, 1f)] public float baseBrightness;
        [Range(0f, 1f)] public float baseAmbientMix;
        [Range(0f, 1f)] public float baseMusicMix;

        [TextArea(3, 8)] public string promptText;

        public void Normalize()
        {
            userId = string.IsNullOrWhiteSpace(userId) ? "FallbackUser" : userId.Trim();
            mood = string.IsNullOrWhiteSpace(mood) ? "calm" : mood.Trim().ToLowerInvariant();
            ambience = string.IsNullOrWhiteSpace(ambience) ? "forest" : ambience.Trim().ToLowerInvariant();
            tempo = string.IsNullOrWhiteSpace(tempo) ? "slow" : tempo.Trim().ToLowerInvariant();

            if (instruments == null || instruments.Length == 0)
            {
                instruments = new[] { "piano", "pad" };
            }

            noveltyTolerance = Mathf.Clamp01(noveltyTolerance);
            baseIntensity = Mathf.Clamp01(baseIntensity);
            baseDensity = Mathf.Clamp01(baseDensity);
            baseBrightness = Mathf.Clamp01(baseBrightness);
            baseAmbientMix = Mathf.Clamp01(baseAmbientMix);
            baseMusicMix = Mathf.Clamp01(baseMusicMix);
        }

        public AudioParameters ToBaselineParameters()
        {
            return new AudioParameters
            {
                intensity = baseIntensity,
                density = baseDensity,
                brightness = baseBrightness,
                ambientMix = baseAmbientMix,
                musicMix = baseMusicMix
            };
        }
    }
}
