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
                baseIntensity = Mathf.Clamp01(0.2f + (preferences.audioIntensity * 0.55f) + ((ResolveLevel(preferences.volumePreference, 0.2f, 0.5f, 0.8f) - 0.5f) * 0.25f) + GetMoodIntensityOffset(preferences.mood) + GetTempoIntensityOffset(preferences.tempoPreference)),
                baseDensity = Mathf.Clamp01((ResolveLevel(preferences.rhythmPreference, 0.2f, 0.5f, 0.75f) * 0.55f) + (preferences.audioIntensity * 0.20f) + GetInstrumentDensityOffset(preferences.preferredInstruments) + GetNoveltyDensityOffset(preferences.noveltyTolerance)),
                baseBrightness = Mathf.Clamp01(ResolveBrightness(preferences.brightnessPreference) + (GetMoodBrightnessOffset(preferences.mood) * 0.35f) + (GetAmbienceBrightnessOffset(preferences.preferredAmbience) * 0.25f) - (preferences.avoidDissonance ? 0.03f : 0f)),
                baseTempo = ResolveTempo(preferences.tempoPreference),
                baseFade = ResolveFade(preferences.tempoPreference, preferences.noveltyTolerance, preferences.reverbPreference),
                baseAmbientMix = ResolveAmbientMix(preferences.ambientMusicBalance),
                baseMusicMix = 1f - ResolveAmbientMix(preferences.ambientMusicBalance),
                rhythmAmount = ResolveLevel(preferences.rhythmPreference, 0.2f, 0.5f, 0.8f),
                natureLevel = ResolveLevel(preferences.natureSoundPreference, 0f, 0.55f, 0.85f),
                reverbAmount = ResolveLevel(preferences.reverbPreference, 0.2f, 0.5f, 0.8f),
                volumeLevel = ResolveLevel(preferences.volumePreference, 0.25f, 0.55f, 0.8f),
                relaxationResponsiveness = ResolveRelaxationResponsiveness(preferences),
                confidenceSensitivity = Mathf.Lerp(0.55f, 0.85f, 1f - preferences.noveltyTolerance)
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

        private static float ResolveTempo(string tempo)
        {
            switch (tempo)
            {
                case "fast":
                    return 0.8f;
                case "medium":
                    return 0.5f;
                default:
                    return 0.2f;
            }
        }

        private static float ResolveFade(string tempo, float noveltyTolerance, string reverbPreference)
        {
            float tempoFade = tempo == "fast" ? 0.42f : tempo == "medium" ? 0.58f : 0.78f;
            float reverbBias = reverbPreference == "spacious" ? 0.08f : reverbPreference == "dry" ? -0.08f : 0f;
            return Mathf.Clamp01(tempoFade + reverbBias - (noveltyTolerance * 0.12f));
        }

        private static float ResolveAmbientMix(string balance)
        {
            switch (balance)
            {
                case "mostly_ambience":
                case "mostly ambience":
                    return 0.8f;
                case "mostly_music":
                case "mostly music":
                    return 0.2f;
                default:
                    return 0.5f;
            }
        }

        private static float ResolveBrightness(string preference)
        {
            switch (preference)
            {
                case "bright_clear":
                case "bright/clear":
                case "bright":
                    return 0.8f;
                case "neutral":
                    return 0.5f;
                default:
                    return 0.2f;
            }
        }

        private static float ResolveLevel(string value, float low, float medium, float high)
        {
            switch (value)
            {
                case "high":
                case "more_motion":
                case "more motion":
                case "spacious":
                    return high;
                case "medium":
                case "gentle_pulse":
                case "gentle pulse":
                case "balanced":
                    return medium;
                default:
                    return low;
            }
        }

        private static float ResolveRelaxationResponsiveness(UserPreferences preferences)
        {
            float moodBias = preferences.mood == "sleepy" ? 0.12f : preferences.mood == "calm" ? 0.08f : 0f;
            return Mathf.Clamp01(0.62f + moodBias + ((1f - preferences.audioIntensity) * 0.12f));
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
            builder.Append(". Rhythm amount ");
            builder.Append(profile.rhythmAmount.ToString("F2"));
            builder.Append(", nature level ");
            builder.Append(profile.natureLevel.ToString("F2"));
            builder.Append(", spaciousness ");
            builder.Append(profile.reverbAmount.ToString("F2"));
            builder.Append(", preferred volume ");
            builder.Append(profile.volumeLevel.ToString("F2"));
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
