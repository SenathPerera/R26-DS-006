using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    public static class AudioRLStateEncoder
    {
        public const int ObservationDimension = 34;
        public const int PreferenceDimension = 13;

        public static float[] EncodeForImportedPolicy(AudioRLState state, float maximumActionDelta)
        {
            float[] observation = new float[ObservationDimension];
            float[] preferences = state.preferenceEncoding ?? new float[PreferenceDimension];
            float[] current = state.currentParameters.ToControlVector();
            float[] recent = state.recentMeanResidualAction.ToArray();
            float safeMaximumDelta = Mathf.Max(0.0001f, maximumActionDelta);
            int index = 0;

            for (int i = 0; i < PreferenceDimension; i++)
            {
                observation[index++] = Mathf.Clamp01(i < preferences.Length ? preferences[i] : 0f);
            }

            for (int i = 0; i < current.Length; i++)
            {
                observation[index++] = Mathf.Clamp01(current[i]);
            }

            observation[index++] = Mathf.Clamp01(state.signal.stress);
            observation[index++] = Mathf.Clamp01(state.signal.confidence);
            observation[index++] = Mathf.Clamp01(state.stressTrend + 0.5f);
            observation[index++] = Mathf.Clamp01(state.confidenceTrend + 0.5f);

            for (int i = 0; i < recent.Length; i++)
            {
                observation[index++] = Mathf.Clamp01(recent[i] + 0.5f);
            }

            observation[index++] = Mathf.Clamp01(state.sessionProgress);
            observation[index++] = Mathf.Clamp01(state.noveltyCount / 20f);
            observation[index] = Mathf.Clamp01(state.recentMeanResidualAction.MeanAbsoluteMagnitude / safeMaximumDelta);
            return observation;
        }

        public static float[] BuildPreferenceEncoding(AudioProfile profile, AudioParameters baseline)
        {
            float tempo = baseline.tempo;
            float rhythm = profile != null ? profile.rhythmAmount : Mathf.Clamp01((tempo * 0.65f) + (baseline.density * 0.35f));
            float nature = profile != null ? profile.natureLevel : ResolveNatureLevel(null);
            float reverb = profile != null ? profile.reverbAmount : ResolveReverbLevel(null);
            float novelty = profile != null ? profile.noveltyTolerance : 0.2f;
            float dissonance = profile != null && !profile.avoidDissonance ? 0.45f : 0.15f;
            float responsiveness = profile != null ? profile.relaxationResponsiveness : ResolveRelaxationResponsiveness(null, baseline);
            float confidenceSensitivity = profile != null ? profile.confidenceSensitivity : Mathf.Lerp(0.55f, 0.85f, 1f - novelty);

            return new[]
            {
                baseline.intensity,
                baseline.density,
                baseline.brightness,
                tempo,
                baseline.musicMix,
                baseline.ambientMix,
                rhythm,
                nature,
                reverb,
                Mathf.Clamp01(novelty),
                dissonance,
                responsiveness,
                confidenceSensitivity
            };
        }

        private static float ResolveNatureLevel(string ambience)
        {
            switch (ambience)
            {
                case "forest":
                case "ocean":
                case "rain":
                    return 0.85f;
                case "temple":
                    return 0.55f;
                case "studio":
                    return 0.15f;
                default:
                    return 0.35f;
            }
        }

        private static float ResolveReverbLevel(string ambience)
        {
            switch (ambience)
            {
                case "temple":
                case "ocean":
                    return 0.78f;
                case "forest":
                    return 0.62f;
                case "studio":
                    return 0.35f;
                default:
                    return 0.55f;
            }
        }

        private static float ResolveRelaxationResponsiveness(AudioProfile profile, AudioParameters baseline)
        {
            float moodBias = profile != null && profile.mood == "sleepy" ? 0.12f : profile != null && profile.mood == "calm" ? 0.08f : 0f;
            return Mathf.Clamp01(0.62f + moodBias + ((1f - baseline.intensity) * 0.12f));
        }
    }
}
