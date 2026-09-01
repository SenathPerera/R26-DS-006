using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    [Serializable]
    public class UserPreferences
    {
        public string userId = "FallbackUser";
        public string[] preferredInstruments = { "piano", "pad" };
        public string mood = "calm";
        public string tempoPreference = "slow";
        public string preferredAmbience = "forest";
        [Range(0f, 1f)] public float audioIntensity = 0.3f;
        [Range(0f, 1f)] public float noveltyTolerance = 0.2f;
        public bool avoidDissonance = true;

        public void Normalize()
        {
            userId = string.IsNullOrWhiteSpace(userId) ? "FallbackUser" : userId.Trim();
            mood = NormalizeKeyword(mood, "calm");
            tempoPreference = NormalizeKeyword(tempoPreference, "slow");
            preferredAmbience = NormalizeKeyword(preferredAmbience, "forest");
            audioIntensity = Mathf.Clamp01(audioIntensity);
            noveltyTolerance = Mathf.Clamp01(noveltyTolerance);

            if (preferredInstruments == null || preferredInstruments.Length == 0)
            {
                preferredInstruments = new[] { "piano", "pad" };
                return;
            }

            for (int i = 0; i < preferredInstruments.Length; i++)
            {
                preferredInstruments[i] = NormalizeKeyword(preferredInstruments[i], "pad");
            }
        }

        public static UserPreferences CreateSafeDefaults()
        {
            var defaults = new UserPreferences();
            defaults.Normalize();
            return defaults;
        }

        public string InstrumentsAsDisplayString()
        {
            if (preferredInstruments == null || preferredInstruments.Length == 0)
            {
                return "piano, pad";
            }

            return string.Join(", ", preferredInstruments);
        }

        private static string NormalizeKeyword(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        }
    }
}
