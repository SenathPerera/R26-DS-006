using System.Collections.Generic;
using System.Text;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL
{
    public class LyriaPromptBuilder : MonoBehaviour
    {
        [Header("VR Environment Context")]
        [SerializeField] private string environmentId = "default";
        [SerializeField] private string environmentDisplayName = "Meditation Environment";
        [SerializeField, TextArea(2, 4)] private string environmentMusicReference =
            "calm instrumental music that fits the selected meditation environment";

        public string EnvironmentId => string.IsNullOrWhiteSpace(environmentId) ? "default" : environmentId.Trim();
        public string EnvironmentDisplayName => string.IsNullOrWhiteSpace(environmentDisplayName)
            ? "Meditation Environment"
            : environmentDisplayName.Trim();
        public string EnvironmentMusicReference => string.IsNullOrWhiteSpace(environmentMusicReference)
            ? $"calm instrumental music for {EnvironmentDisplayName}"
            : environmentMusicReference.Trim();

        public void ConfigureEnvironment(string id, string displayName, string musicReference)
        {
            environmentId = string.IsNullOrWhiteSpace(id) ? "default" : id.Trim();
            environmentDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? "Meditation Environment"
                : displayName.Trim();
            environmentMusicReference = string.IsNullOrWhiteSpace(musicReference)
                ? $"calm instrumental music for {environmentDisplayName}"
                : musicReference.Trim();
        }

        public LyriaControlFrame BuildFrame(
            AudioProfile profile,
            PersonalizationStrategy strategy,
            AudioParameters parameters,
            AdaptiveControllerMode mode,
            SignalPacket signal,
            string actionName,
            float latestReward)
        {
            strategy ??= PersonalizationStrategy.CreateNeutral();

            var prompts = new List<PromptWeight>();
            UpsertPrompt(prompts, "instrumental meditation", 1.2f);
            UpsertPrompt(prompts, $"music for {EnvironmentDisplayName}", 1.10f);
            UpsertPrompt(prompts, EnvironmentMusicReference, 1.05f);
            UpsertPrompt(prompts, profile != null ? profile.mood : "calm", 0.90f);
            UpsertPrompt(prompts, GetTempoPrompt(profile), 0.65f);
            UpsertPrompt(prompts, GetAmbiencePrompt(profile, parameters), 0.55f + (parameters.ambientMix * 0.45f));

            if (profile != null && profile.instruments != null)
            {
                foreach (string instrument in profile.instruments)
                {
                    UpsertPrompt(prompts, instrument, 0.60f + (parameters.musicMix * 0.30f));
                }
            }

            if (profile != null && profile.avoidDissonance)
            {
                UpsertPrompt(prompts, "gentle consonant harmony", 0.85f);
            }
            else
            {
                UpsertPrompt(prompts, "subtle harmonic motion", 0.45f);
            }

            if (strategy.accentPrompts != null)
            {
                for (int i = 0; i < strategy.accentPrompts.Length; i++)
                {
                    UpsertPrompt(prompts, strategy.accentPrompts[i], 0.48f + (0.08f * i));
                }
            }

            UpsertPrompt(prompts, GetActionPrompt(mode, actionName), 0.62f);

            var config = new LyriaGenerationConfig
            {
                bpm = ResolveBpm(profile, strategy, parameters),
                density = parameters.density,
                brightness = parameters.brightness,
                guidance = ResolveGuidance(profile, strategy),
                temperature = ResolveTemperature(profile, strategy),
                topK = ResolveTopK(profile),
                scale = ResolveScale(profile),
                muteBass = strategy.muteBass,
                muteDrums = strategy.muteDrums || (profile != null && profile.tempo == "slow"),
                onlyBassAndDrums = false,
                musicGenerationMode = ResolveGenerationMode(profile)
            }.Normalize();

            var frame = new LyriaControlFrame
            {
                environmentId = EnvironmentId,
                environmentDisplayName = EnvironmentDisplayName,
                strategyName = strategy.displayName,
                actionName = string.IsNullOrWhiteSpace(actionName) ? mode.ToString() : actionName,
                latestReward = latestReward,
                weightedPrompts = prompts.ToArray(),
                config = config,
                promptSummary = BuildPromptSummary(
                    profile,
                    strategy,
                    parameters,
                    signal,
                    actionName,
                    latestReward,
                    config,
                    EnvironmentDisplayName)
            };

            frame.Normalize();
            return frame;
        }

        private static string BuildPromptSummary(
            AudioProfile profile,
            PersonalizationStrategy strategy,
            AudioParameters parameters,
            SignalPacket signal,
            string actionName,
            float latestReward,
            LyriaGenerationConfig config,
            string environmentName)
        {
            var builder = new StringBuilder();
            builder.Append(strategy.displayName);
            builder.Append(": ");
            builder.Append("environment ");
            builder.Append(environmentName);
            builder.Append(", ");
            builder.Append(profile != null ? profile.mood : "calm");
            builder.Append(", ");
            builder.Append(profile != null ? profile.tempo : "slow");
            builder.Append(" meditation with ");
            builder.Append(profile != null && profile.instruments != null && profile.instruments.Length > 0
                ? string.Join(", ", profile.instruments)
                : "piano and pad");
            builder.Append(". Ambience leans ");
            builder.Append(profile != null ? profile.ambience : "forest");
            builder.Append(". Current steering is ");
            builder.Append(string.IsNullOrWhiteSpace(actionName) ? "stabilization" : actionName.ToLowerInvariant());
            builder.Append(". Stress ");
            builder.Append(signal.stress.ToString("F2"));
            builder.Append(", confidence ");
            builder.Append(signal.confidence.ToString("F2"));
            builder.Append(", reward ");
            builder.Append(latestReward.ToString("F2"));
            builder.Append(". Target BPM ");
            builder.Append(config.bpm);
            builder.Append(", density ");
            builder.Append(parameters.density.ToString("F2"));
            builder.Append(", brightness ");
            builder.Append(parameters.brightness.ToString("F2"));
            builder.Append(".");
            return builder.ToString();
        }

        private static string GetTempoPrompt(AudioProfile profile)
        {
            string tempo = profile != null ? profile.tempo : "slow";
            switch (tempo)
            {
                case "fast":
                    return "gentle forward motion";
                case "medium":
                    return "steady meditative pulse";
                default:
                    return "slow breath-paced flow";
            }
        }

        private static string GetAmbiencePrompt(AudioProfile profile, AudioParameters parameters)
        {
            string ambience = profile != null ? profile.ambience : "forest";
            return parameters.ambientMix >= parameters.musicMix
                ? $"{ambience}-inspired atmosphere"
                : $"{ambience}-tinted musical texture";
        }

        private static string GetActionPrompt(AdaptiveControllerMode mode, string actionName)
        {
            if (!string.IsNullOrWhiteSpace(actionName))
            {
                return actionName.ToLowerInvariant();
            }

            switch (mode)
            {
                case AdaptiveControllerMode.HighStressAdaptive:
                    return "slightly fuller supportive motion";
                case AdaptiveControllerMode.LowStressCalming:
                    return "softer lighter grounding";
                case AdaptiveControllerMode.LowConfidenceDampened:
                    return "stable safe baseline";
                default:
                    return "balanced adaptive meditation";
            }
        }

        private static int ResolveBpm(AudioProfile profile, PersonalizationStrategy strategy, AudioParameters parameters)
        {
            int baseBpm = 68;
            if (profile != null)
            {
                switch (profile.tempo)
                {
                    case "fast":
                        baseBpm = 108;
                        break;
                    case "medium":
                        baseBpm = 86;
                        break;
                    default:
                        baseBpm = 68;
                        break;
                }
            }

            int policyBpm = Mathf.RoundToInt(Mathf.Lerp(60f, 112f, parameters.tempo));
            int profileAndPolicyBpm = Mathf.RoundToInt((baseBpm + policyBpm) * 0.5f);
            return Mathf.Clamp(profileAndPolicyBpm + strategy.bpmOffset, 60, 200);
        }

        private static float ResolveGuidance(AudioProfile profile, PersonalizationStrategy strategy)
        {
            float noveltyTolerance = profile != null ? profile.noveltyTolerance : 0.2f;
            bool avoidDissonance = profile == null || profile.avoidDissonance;
            float guidance = 3.1f + strategy.guidanceBias - (noveltyTolerance * 0.25f) + (avoidDissonance ? 0.45f : 0f);
            return Mathf.Clamp(guidance, 0f, 6f);
        }

        private static float ResolveTemperature(AudioProfile profile, PersonalizationStrategy strategy)
        {
            float noveltyTolerance = profile != null ? profile.noveltyTolerance : 0.2f;
            float temperature = 0.75f + (noveltyTolerance * 0.85f) + strategy.temperatureBias;
            return Mathf.Clamp(temperature, 0f, 3f);
        }

        private static int ResolveTopK(AudioProfile profile)
        {
            float noveltyTolerance = profile != null ? profile.noveltyTolerance : 0.2f;
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(24f, 96f, noveltyTolerance)), 1, 1000);
        }

        private static LyriaScale ResolveScale(AudioProfile profile)
        {
            if (profile == null)
            {
                return LyriaScale.C_MAJOR_A_MINOR;
            }

            switch (profile.mood)
            {
                case "sleepy":
                    return LyriaScale.F_MAJOR_D_MINOR;
                case "focused":
                    return LyriaScale.G_MAJOR_E_MINOR;
                case "energized":
                    return LyriaScale.D_MAJOR_B_MINOR;
                default:
                    return profile.avoidDissonance ? LyriaScale.C_MAJOR_A_MINOR : LyriaScale.G_MAJOR_E_MINOR;
            }
        }

        private static LyriaGenerationMode ResolveGenerationMode(AudioProfile profile)
        {
            if (profile == null)
            {
                return LyriaGenerationMode.QUALITY;
            }

            return profile.noveltyTolerance >= 0.6f
                ? LyriaGenerationMode.DIVERSITY
                : LyriaGenerationMode.QUALITY;
        }

        private static void UpsertPrompt(List<PromptWeight> prompts, string text, float weight)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string normalizedText = text.Trim().ToLowerInvariant();
            for (int i = 0; i < prompts.Count; i++)
            {
                if (prompts[i].text == normalizedText)
                {
                    PromptWeight existing = prompts[i];
                    existing.weight = Mathf.Clamp(existing.weight + weight, 0.01f, 2.5f);
                    prompts[i] = existing;
                    return;
                }
            }

            prompts.Add(new PromptWeight(normalizedText, weight));
        }
    }
}
