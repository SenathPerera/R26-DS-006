using System.Text;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Profile
{
    public class ProfileEngine : MonoBehaviour
    {
        [SerializeField] private bool logToConsole = true;

        public AudioProfile CurrentProfile { get; private set; }

        public AudioProfile GenerateProfile(UserPreferences preferences)
        {
            if (preferences == null)
            {
                preferences = UserPreferences.CreateSafeDefaults();
            }

            preferences.Normalize();

            var profile = new AudioProfile
            {
                userId = preferences.userId,
                mood = preferences.mood,
                ambience = preferences.preferredAmbience,
                tempo = preferences.tempoPreference,
                instruments = preferences.preferredInstruments,
                avoidDissonance = preferences.avoidDissonance,
                noveltyTolerance = preferences.noveltyTolerance,
                baseIntensity = Mathf.Clamp01(0.2f + (preferences.audioIntensity * 0.55f) + GetMoodIntensityOffset(preferences.mood) + GetTempoIntensityOffset(preferences.tempoPreference)),
                baseDensity = Mathf.Clamp01(0.25f + (preferences.audioIntensity * 0.40f) + GetInstrumentDensityOffset(preferences.preferredInstruments) + GetNoveltyDensityOffset(preferences.noveltyTolerance)),
                baseBrightness = Mathf.Clamp01(0.28f + GetMoodBrightnessOffset(preferences.mood) + GetAmbienceBrightnessOffset(preferences.preferredAmbience) + GetNoveltyBrightnessOffset(preferences.noveltyTolerance) - (preferences.avoidDissonance ? 0.08f : 0f)),
                baseAmbientMix = Mathf.Clamp01(0.45f + GetAmbienceMixOffset(preferences.preferredAmbience) - (preferences.audioIntensity * 0.15f)),
                baseMusicMix = Mathf.Clamp01(0.55f + (preferences.audioIntensity * 0.10f) + GetInstrumentMusicOffset(preferences.preferredInstruments) - GetAmbienceMusicOffset(preferences.preferredAmbience))
            };

            BalanceMixes(ref profile);
            profile.promptText = BuildPromptText(profile);
            profile.Normalize();
            CurrentProfile = profile;

            if (logToConsole)
            {
                Debug.Log($"[ProfileEngine] Audio profile generated for {profile.userId}.", this);
            }

            return CurrentProfile;
        }

        private static void BalanceMixes(ref AudioProfile profile)
        {
            float total = profile.baseAmbientMix + profile.baseMusicMix;
            if (total <= 0.001f)
            {
                profile.baseAmbientMix = 0.5f;
                profile.baseMusicMix = 0.5f;
                return;
            }

            profile.baseAmbientMix /= total;
            profile.baseMusicMix /= total;
        }

        private static string BuildPromptText(AudioProfile profile)
        {
            var builder = new StringBuilder();
            builder.Append("Personalized meditation audio with a ");
            builder.Append(profile.mood);
            builder.Append(" mood, ");
            builder.Append(profile.tempo);
            builder.Append(" pacing, and ");
            builder.Append(profile.ambience);
            builder.Append(" ambience. Use ");
            builder.Append(profile.instruments != null && profile.instruments.Length > 0 ? string.Join(", ", profile.instruments) : "piano and pad");
            builder.Append(". Keep the overall energy around ");
            builder.Append(profile.baseIntensity.ToString("F2"));
            builder.Append(", texture density around ");
            builder.Append(profile.baseDensity.ToString("F2"));
            builder.Append(", brightness around ");
            builder.Append(profile.baseBrightness.ToString("F2"));
            builder.Append(". Favor ");
            builder.Append(profile.baseMusicMix >= profile.baseAmbientMix ? "meditation music" : "environment ambience");
            builder.Append(" in the base mix. ");
            builder.Append(profile.avoidDissonance ? "Avoid dissonant tension." : "Allow mild harmonic tension.");
            builder.Append(" Novelty tolerance is ");
            builder.Append(profile.noveltyTolerance.ToString("F2"));
            builder.Append(".");
            return builder.ToString();
        }

        private static float GetMoodIntensityOffset(string mood)
        {
            switch (mood)
            {
                case "energized":
                case "focused":
                    return 0.12f;
                case "anxious":
                    return 0.05f;
                case "calm":
                    return -0.05f;
                case "sleepy":
                    return -0.10f;
                default:
                    return 0f;
            }
        }

        private static float GetTempoIntensityOffset(string tempo)
        {
            switch (tempo)
            {
                case "fast":
                    return 0.14f;
                case "medium":
                    return 0.05f;
                case "slow":
                    return -0.06f;
                default:
                    return 0f;
            }
        }

        private static float GetInstrumentDensityOffset(string[] instruments)
        {
            if (instruments == null)
            {
                return 0f;
            }

            float offset = 0f;
            foreach (string instrument in instruments)
            {
                switch (instrument)
                {
                    case "drums":
                    case "percussion":
                        offset += 0.10f;
                        break;
                    case "strings":
                    case "pad":
                        offset += 0.06f;
                        break;
                    case "flute":
                    case "bells":
                        offset -= 0.03f;
                        break;
                }
            }

            return Mathf.Clamp(offset, -0.08f, 0.18f);
        }

        private static float GetNoveltyDensityOffset(float noveltyTolerance)
        {
            return Mathf.Lerp(-0.03f, 0.08f, noveltyTolerance);
        }

        private static float GetMoodBrightnessOffset(string mood)
        {
            switch (mood)
            {
                case "focused":
                case "energized":
                    return 0.12f;
                case "calm":
                    return -0.04f;
                case "sleepy":
                    return -0.10f;
                default:
                    return 0f;
            }
        }

        private static float GetAmbienceBrightnessOffset(string ambience)
        {
            switch (ambience)
            {
                case "forest":
                    return -0.02f;
                case "ocean":
                    return 0.01f;
                case "rain":
                    return -0.06f;
                case "space":
                    return 0.10f;
                case "temple":
                    return -0.01f;
                default:
                    return 0f;
            }
        }

        private static float GetNoveltyBrightnessOffset(float noveltyTolerance)
        {
            return Mathf.Lerp(-0.02f, 0.10f, noveltyTolerance);
        }

        private static float GetAmbienceMixOffset(string ambience)
        {
            switch (ambience)
            {
                case "forest":
                case "rain":
                case "ocean":
                    return 0.15f;
                case "temple":
                    return 0.08f;
                case "studio":
                    return -0.10f;
                default:
                    return 0f;
            }
        }

        private static float GetInstrumentMusicOffset(string[] instruments)
        {
            if (instruments == null || instruments.Length == 0)
            {
                return 0f;
            }

            float offset = 0f;
            foreach (string instrument in instruments)
            {
                switch (instrument)
                {
                    case "piano":
                    case "strings":
                    case "flute":
                        offset += 0.03f;
                        break;
                    case "drone":
                    case "pad":
                        offset += 0.01f;
                        break;
                }
            }

            return Mathf.Clamp(offset, 0f, 0.10f);
        }

        private static float GetAmbienceMusicOffset(string ambience)
        {
            switch (ambience)
            {
                case "forest":
                case "rain":
                case "ocean":
                    return 0.06f;
                default:
                    return 0f;
            }
        }
    }
}
