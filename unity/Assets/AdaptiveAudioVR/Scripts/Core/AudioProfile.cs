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
        [Range(0f, 1f)] public float baseTempo;
        [Range(0f, 1f)] public float baseFade;
        [Range(0f, 1f)] public float baseAmbientMix;
        [Range(0f, 1f)] public float baseMusicMix;
        [Range(0f, 1f)] public float rhythmAmount;
        [Range(0f, 1f)] public float natureLevel;
        [Range(0f, 1f)] public float reverbAmount;
        [Range(0f, 1f)] public float volumeLevel;
        [Range(0f, 1f)] public float relaxationResponsiveness;
        [Range(0f, 1f)] public float confidenceSensitivity;

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
            baseTempo = Mathf.Clamp01(baseTempo);
            baseFade = Mathf.Clamp01(baseFade);
            baseAmbientMix = Mathf.Clamp01(baseAmbientMix);
            baseMusicMix = Mathf.Clamp01(baseMusicMix);
            rhythmAmount = Mathf.Clamp01(rhythmAmount);
            natureLevel = Mathf.Clamp01(natureLevel);
            reverbAmount = Mathf.Clamp01(reverbAmount);
            volumeLevel = Mathf.Clamp01(volumeLevel);
            relaxationResponsiveness = Mathf.Clamp01(relaxationResponsiveness);
            confidenceSensitivity = Mathf.Clamp01(confidenceSensitivity);
        }

        public AudioParameters ToBaselineParameters()
        {
            return new AudioParameters
            {
                intensity = baseIntensity,
                density = baseDensity,
                brightness = baseBrightness,
                tempo = baseTempo,
                fade = baseFade,
                ambientMix = baseAmbientMix,
                musicMix = baseMusicMix
            };
        }
    }
}
