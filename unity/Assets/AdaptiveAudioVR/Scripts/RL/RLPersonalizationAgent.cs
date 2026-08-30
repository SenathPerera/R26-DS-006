using System;
using System.Collections.Generic;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL
{
    public class RLPersonalizationAgent : MonoBehaviour
    {
        [SerializeField] private float explorationStrength = 0.35f;
        [SerializeField] private bool logToConsole = true;

        public PersonalizationStrategy CurrentStrategy =>
            currentArmIndex >= 0 && currentArmIndex < arms.Count
                ? arms[currentArmIndex].strategy
                : PersonalizationStrategy.CreateNeutral();

        public string CurrentStrategyName => CurrentStrategy.displayName;

        private readonly List<BanditArm> arms = new List<BanditArm>();
        private int currentArmIndex = -1;
        private int totalSelections;

        private void Awake()
        {
            EnsureDefaultStrategies();
        }

        public PersonalizationStrategy SelectStrategy(UserPreferences preferences, AudioProfile profile)
        {
            EnsureDefaultStrategies();

            if (preferences == null || profile == null)
            {
                currentArmIndex = 0;
                return CurrentStrategy;
            }

            float bestScore = float.MinValue;
            int bestIndex = 0;

            for (int i = 0; i < arms.Count; i++)
            {
                BanditArm arm = arms[i];
                float priorScore = GetContextualPrior(arm.strategy, preferences, profile);
                float confidenceBonus = explorationStrength * Mathf.Sqrt(Mathf.Log(totalSelections + 2f) / (arm.selectionCount + 1f));
                float score = priorScore + arm.meanReward + confidenceBonus;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            currentArmIndex = bestIndex;
            arms[bestIndex].selectionCount++;
            totalSelections++;

            if (logToConsole)
            {
                Debug.Log($"[RLPersonalizationAgent] Selected strategy {arms[bestIndex].strategy.displayName} with score {bestScore:F3}.", this);
            }

            return arms[bestIndex].strategy;
        }

        public void UpdateCurrentStrategy(float reward)
        {
            if (currentArmIndex < 0 || currentArmIndex >= arms.Count || float.IsNaN(reward) || float.IsInfinity(reward))
            {
                return;
            }

            BanditArm arm = arms[currentArmIndex];
            arm.rewardObservations++;
            arm.meanReward += (reward - arm.meanReward) / arm.rewardObservations;
        }

        private float GetContextualPrior(PersonalizationStrategy strategy, UserPreferences preferences, AudioProfile profile)
        {
            float score = 0.15f;

            if (TagMatch(strategy.affinityTags, profile.mood))
            {
                score += 0.28f;
            }
            else if (profile.mood == "calm" && strategy.intensityBias <= 0f)
            {
                score += 0.12f;
            }
            else if ((profile.mood == "focused" || profile.mood == "energized") && strategy.intensityBias >= 0f)
            {
                score += 0.12f;
            }

            if (TagMatch(strategy.affinityTags, profile.tempo))
            {
                score += 0.18f;
            }
            else if (profile.tempo == "slow" && strategy.bpmOffset <= 0)
            {
                score += 0.08f;
            }
            else if (profile.tempo == "fast" && strategy.bpmOffset >= 0)
            {
                score += 0.08f;
            }

            if (TagMatch(strategy.affinityTags, profile.ambience))
            {
                score += 0.24f;
            }

            if (profile.instruments != null)
            {
                foreach (string instrument in profile.instruments)
                {
                    if (TagMatch(strategy.affinityTags, instrument))
                    {
                        score += 0.10f;
                    }
                }
            }

            if (preferences.avoidDissonance && strategy.muteDrums)
            {
                score += 0.06f;
            }

            if (preferences.noveltyTolerance <= 0.35f && strategy.temperatureBias <= 0f)
            {
                score += 0.08f;
            }
            else if (preferences.noveltyTolerance >= 0.6f && strategy.temperatureBias >= 0f)
            {
                score += 0.08f;
            }

            if (preferences.audioIntensity <= 0.4f && strategy.intensityBias <= 0.05f)
            {
                score += 0.08f;
            }
            else if (preferences.audioIntensity >= 0.6f && strategy.intensityBias >= 0.05f)
            {
                score += 0.08f;
            }

            return score;
        }

        private static bool TagMatch(string[] tags, string candidate)
        {
            if (tags == null || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            string normalizedCandidate = candidate.Trim().ToLowerInvariant();
            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(tags[i], normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureDefaultStrategies()
        {
            if (arms.Count > 0)
            {
                return;
            }

            arms.Add(new BanditArm
            {
                strategy = new PersonalizationStrategy
                {
                    strategyId = "gentle_forest_piano",
                    displayName = "Gentle Forest Piano",
                    summary = "Low-intensity grounded strategy with airy piano phrasing and nature-forward balance.",
                    affinityTags = new[] { "calm", "slow", "forest", "piano", "flute" },
                    accentPrompts = new[] { "soft piano arpeggios", "forest-inspired air", "breath-paced stillness" },
                    intensityBias = -0.05f,
                    densityBias = -0.06f,
                    brightnessBias = -0.02f,
                    ambientMixBias = 0.12f,
                    musicMixBias = -0.04f,
                    bpmOffset = -6,
                    guidanceBias = 0.35f,
                    temperatureBias = -0.15f,
                    muteDrums = true
                }
            });

            arms.Add(new BanditArm
            {
                strategy = new PersonalizationStrategy
                {
                    strategyId = "airy_flute_canopy",
                    displayName = "Airy Flute Canopy",
                    summary = "A floating texture that favors flute detail and clean melodic space.",
                    affinityTags = new[] { "calm", "slow", "forest", "flute", "nature" },
                    accentPrompts = new[] { "soft flute lines", "open natural air", "light canopy shimmer" },
                    intensityBias = -0.03f,
                    densityBias = -0.02f,
                    brightnessBias = 0.04f,
                    ambientMixBias = 0.08f,
                    musicMixBias = 0.01f,
                    bpmOffset = -4,
                    guidanceBias = 0.25f,
                    temperatureBias = -0.05f,
                    muteDrums = true
                }
            });

            arms.Add(new BanditArm
            {
                strategy = new PersonalizationStrategy
                {
                    strategyId = "warm_rain_pad",
                    displayName = "Warm Rain Pad",
                    summary = "Deeper, darker ambience with gentle pads and minimal rhythmic movement.",
                    affinityTags = new[] { "calm", "sleepy", "slow", "rain", "pad" },
                    accentPrompts = new[] { "warm sustained pads", "soft rain atmosphere", "dark gentle resonance" },
                    intensityBias = -0.08f,
                    densityBias = -0.03f,
                    brightnessBias = -0.10f,
                    ambientMixBias = 0.10f,
                    musicMixBias = 0f,
                    bpmOffset = -8,
                    guidanceBias = 0.40f,
                    temperatureBias = -0.18f,
                    muteBass = true,
                    muteDrums = true
                }
            });

            arms.Add(new BanditArm
            {
                strategy = new PersonalizationStrategy
                {
                    strategyId = "focused_reflection",
                    displayName = "Focused Reflection",
                    summary = "Cleaner and brighter strategy for concentrated attention and more present melody.",
                    affinityTags = new[] { "focused", "energized", "medium", "studio", "piano" },
                    accentPrompts = new[] { "clear piano motifs", "focused motion", "clean harmonic detail" },
                    intensityBias = 0.06f,
                    densityBias = 0.04f,
                    brightnessBias = 0.08f,
                    ambientMixBias = -0.05f,
                    musicMixBias = 0.05f,
                    bpmOffset = 6,
                    guidanceBias = 0.10f,
                    temperatureBias = 0.08f,
                    muteDrums = false
                }
            });

            arms.Add(new BanditArm
            {
                strategy = new PersonalizationStrategy
                {
                    strategyId = "ocean_drift",
                    displayName = "Ocean Drift",
                    summary = "Spacious strategy with broad ambience and smooth motion for longer exhalation patterns.",
                    affinityTags = new[] { "calm", "slow", "ocean", "pad", "ambient" },
                    accentPrompts = new[] { "ocean-inspired wash", "wide ambient bed", "slow tide motion" },
                    intensityBias = -0.02f,
                    densityBias = -0.05f,
                    brightnessBias = 0.01f,
                    ambientMixBias = 0.15f,
                    musicMixBias = -0.06f,
                    bpmOffset = -5,
                    guidanceBias = 0.20f,
                    temperatureBias = -0.05f,
                    muteDrums = true
                }
            });
        }

        [Serializable]
        private class BanditArm
        {
            public PersonalizationStrategy strategy;
            public int selectionCount;
            public int rewardObservations;
            public float meanReward;
        }
    }
}
