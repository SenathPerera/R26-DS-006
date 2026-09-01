using System;
using System.IO;
using System.Reflection;
using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.RL;
using AdaptiveAudioVR.RL.Agent;
using UnityEditor;
using UnityEngine;

namespace AdaptiveAudioVR.Editor
{
    public static class AudioRLAgentVerification
    {
        [MenuItem("AdaptiveAudioVR/Verify Audio RL Agent")]
        public static void RunFromMenu()
        {
            RunAll();
            Debug.Log("[AudioRLAgentVerification] All audio RL agent checks passed.");
        }

        public static void RunBatch()
        {
            try
            {
                RunAll();
                Debug.Log("[AudioRLAgentVerification] All audio RL agent checks passed.");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioRLAgentVerification] Verification failed: {ex}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                else
                {
                    throw;
                }
            }
        }

        private static void RunAll()
        {
            VerifySevenDimensionalActionMapping();
            VerifyImportedObservationContract();
            VerifyDirectPpoPolicyArtifact();
            VerifyImportedPolicyArtifact();
            VerifySafetyGating();
            VerifyDelayedRewardDirection();
            VerifyReplayCapacity();
            VerifyJapaneseTempleGenerationContext();
            VerifyGeneratedClipStartGate();
        }

        private static void VerifySevenDimensionalActionMapping()
        {
            AudioParameters start = BuildParameters(0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.5f, 0.5f);
            AudioRLAction action = new AudioRLAction
            {
                deltaIntensity = 0.02f,
                deltaDensity = -0.02f,
                deltaBrightness = 0.03f,
                deltaTempo = -0.03f,
                deltaFade = 0.04f,
                deltaMusicMix = 0.05f,
                deltaAmbientMix = -0.05f
            };

            AudioParameters result = action.ApplyTo(start);
            Ensure(Approximately(result.intensity, 0.42f), "Intensity action was not applied.");
            Ensure(Approximately(result.density, 0.38f), "Density action was not applied.");
            Ensure(Approximately(result.brightness, 0.43f), "Brightness action was not applied.");
            Ensure(Approximately(result.tempo, 0.37f), "Tempo action was not applied.");
            Ensure(Approximately(result.fade, 0.44f), "Fade action was not applied.");
            Ensure(Approximately(result.musicMix + result.ambientMix, 1f), "Music and ambient mix were not normalized.");
        }

        private static void VerifyImportedObservationContract()
        {
            AudioParameters baseline = BuildParameters(0.3f, 0.3f, 0.3f, 0.2f, 0.7f, 0.45f, 0.55f);
            AudioProfile profile = new AudioProfile
            {
                userId = "verification-user",
                mood = "calm",
                ambience = "forest",
                tempo = "slow",
                noveltyTolerance = 0.2f,
                avoidDissonance = true
            };
            AudioRLState state = BuildState(0.5f, 0.8f, baseline);
            state.preferenceEncoding = AudioRLStateEncoder.BuildPreferenceEncoding(profile, baseline);
            float[] observation = AudioRLStateEncoder.EncodeForImportedPolicy(state, 0.08f);

            Ensure(observation.Length == AudioRLStateEncoder.ObservationDimension, "Imported observation must remain exactly 34 dimensions.");
            for (int i = 0; i < observation.Length; i++)
            {
                Ensure(observation[i] >= 0f && observation[i] <= 1f, $"Observation value {i} was outside [0,1].");
            }
        }

        private static void VerifySafetyGating()
        {
            AudioParameters baseline = BuildParameters(0.3f, 0.3f, 0.3f, 0.2f, 0.7f, 0.45f, 0.55f);
            AudioRLState state = BuildState(0.7f, 0.2f, baseline);
            AudioRLSafetyFilter filter = new AudioRLSafetyFilter(0.08f, 0.25f, 0.45f, 0.30f, 0.60f, 0.30f, 0.05f, 0.15f, 0.85f);
            AudioRLAction proposed = AudioRLAction.FromArray(new[] { 0.08f, 0.08f, 0.08f, 0.08f, 0.08f, 0.08f, -0.08f });

            AudioRLAction previousAction = AudioRLAction.FromArray(new[] { 0.05f, -0.05f, 0.05f, -0.05f, 0.05f, 0.05f, -0.05f });
            AudioRLSafetyResult frozen = filter.Apply(proposed, previousAction, state, true, true);
            Ensure(frozen.safetyMode == AudioRLSafetyMode.ConfidenceFreeze, "Low confidence did not activate ConfidenceFreeze.");
            Ensure(frozen.finalSafeAction.MeanAbsoluteMagnitude <= 0.0001f, "ConfidenceFreeze did not cancel the action.");

            state.signal.confidence = 0.9f;
            state.signal.signalQuality = 0.9f;
            AudioRLSafetyResult normal = filter.Apply(proposed, AudioRLAction.NoChange, state, true, true);
            Ensure(normal.finalSafeAction.MaximumAbsoluteMagnitude <= 0.0801f, "Safety filter exceeded the configured action bound.");
            Ensure(Approximately(normal.safeTarget.musicMix + normal.safeTarget.ambientMix, 1f), "Safety filter produced an invalid mix.");
            AudioParameters reconstructedTarget = normal.finalSafeAction.ApplyTo(state.currentParameters);
            Ensure(Approximately(reconstructedTarget.musicMix, normal.safeTarget.musicMix), "Logged safe mix action did not match the applied target.");
            Ensure(Approximately(reconstructedTarget.tempo, normal.safeTarget.tempo), "Logged safe tempo action did not match the applied target.");

            AudioRLSafetyResult emergency = filter.Apply(proposed, AudioRLAction.NoChange, state, false, true);
            Ensure(emergency.safetyMode == AudioRLSafetyMode.EmergencyMuted, "Emergency mute did not cancel the action.");

            AudioRLSafetyResult stale = filter.Apply(proposed, AudioRLAction.NoChange, state, true, false);
            Ensure(stale.safetyMode == AudioRLSafetyMode.StaleSignalRecovery, "Stale input did not trigger baseline recovery.");
        }

        private static void VerifyImportedPolicyArtifact()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath,
                "AdaptiveAudioVR",
                "Training",
                "ppo_seed_37_unity_policy.json");
            PpoSampledResidualPolicy policy = new PpoSampledResidualPolicy(8);
            Ensure(policy.TryLoad(path, out string error), $"PPO-derived policy artifact did not load: {error}");

            AudioParameters baseline = BuildParameters(0.3f, 0.3f, 0.3f, 0.2f, 0.7f, 0.45f, 0.55f);
            AudioProfile profile = new AudioProfile
            {
                userId = "verification-user",
                mood = "calm",
                ambience = "forest",
                tempo = "slow",
                noveltyTolerance = 0.2f,
                avoidDissonance = true
            };
            AudioRLState state = BuildState(0.5f, 0.8f, baseline);
            state.preferenceEncoding = AudioRLStateEncoder.BuildPreferenceEncoding(profile, baseline);
            AudioRLAction action = policy.GetResidualAction(state);

            Ensure(policy.IsReady, "PPO-derived policy did not report ready after loading.");
            Ensure(action.MaximumAbsoluteMagnitude <= policy.MaximumDelta + 0.0001f, "PPO-derived residual exceeded its exported action bound.");
        }

        private static void VerifyDirectPpoPolicyArtifact()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath,
                "AdaptiveAudioVR",
                "Training",
                "ppo_seed_37_unity_network.json");
            PpoDirectResidualPolicy policy = new PpoDirectResidualPolicy();
            Ensure(policy.TryLoad(path, out string error), $"Direct PPO policy artifact did not load: {error}");

            AudioParameters baseline = BuildParameters(0.3f, 0.3f, 0.3f, 0.2f, 0.7f, 0.45f, 0.55f);
            AudioProfile profile = new AudioProfile
            {
                userId = "verification-user",
                mood = "calm",
                ambience = "forest",
                tempo = "slow",
                noveltyTolerance = 0.2f,
                avoidDissonance = true,
                rhythmAmount = 0.2f,
                natureLevel = 0.85f,
                reverbAmount = 0.8f,
                relaxationResponsiveness = 0.75f,
                confidenceSensitivity = 0.75f
            };
            AudioRLState state = BuildState(0.5f, 0.8f, baseline);
            state.preferenceEncoding = AudioRLStateEncoder.BuildPreferenceEncoding(profile, baseline);
            AudioRLAction action = policy.GetResidualAction(state);

            Ensure(policy.IsReady, "Direct PPO policy did not report ready after loading.");
            Ensure(action.MaximumAbsoluteMagnitude <= policy.MaximumDelta + 0.0001f, "Direct PPO residual exceeded its exported action bound.");
        }

        private static void VerifyDelayedRewardDirection()
        {
            AudioParameters baseline = BuildParameters(0.3f, 0.3f, 0.3f, 0.2f, 0.7f, 0.45f, 0.55f);
            AudioRLState before = BuildState(0.8f, 0.9f, baseline);
            AudioRLState after = BuildState(0.4f, 0.9f, baseline);
            AudioRLRewardCalculator calculator = new AudioRLRewardCalculator(new AudioRLRewardWeights(), 0.08f);
            AudioRLRewardBreakdown reward = calculator.Compute(before, after, AudioRLAction.NoChange, 0);

            Ensure(reward.stressImprovement > 0f, "Stress reduction was not recognized as improvement.");
            Ensure(reward.totalReward > 0f, "A stable, preference-aligned stress reduction should have positive reward.");
        }

        private static void VerifyReplayCapacity()
        {
            AudioRLReplayBuffer replay = new AudioRLReplayBuffer(2);
            replay.Add(new AudioRLTransition { sessionId = "1" });
            replay.Add(new AudioRLTransition { sessionId = "2" });
            replay.Add(new AudioRLTransition { sessionId = "3" });

            Ensure(replay.Count == 2, "Replay buffer did not enforce its capacity.");
            Ensure(replay.Snapshot()[0].sessionId == "2", "Replay buffer did not retain transitions in chronological order.");
            Ensure(replay.Snapshot()[1].sessionId == "3", "Replay buffer did not retain the newest transition.");
        }

        private static void VerifyJapaneseTempleGenerationContext()
        {
            GameObject owner = new GameObject("LyriaPromptBuilder_Verification");
            try
            {
                LyriaPromptBuilder builder = owner.AddComponent<LyriaPromptBuilder>();
                builder.ConfigureEnvironment(
                    "japanese_temple_pond_garden",
                    "Japanese Temple Pond Garden",
                    "serene Japanese temple music with breathy bamboo flute and sparse temple bells");

                AudioProfile profile = new AudioProfile
                {
                    mood = "calm",
                    tempo = "slow",
                    ambience = "temple",
                    instruments = new[] { "flute", "bells" },
                    avoidDissonance = true
                };
                AudioParameters parameters = BuildParameters(0.3f, 0.3f, 0.2f, 0.2f, 0.7f, 0.55f, 0.45f);
                LyriaControlFrame frame = builder.BuildFrame(
                    profile,
                    PersonalizationStrategy.CreateNeutral(),
                    parameters,
                    AdaptiveControllerMode.Initialized,
                    SignalPacket.CreateDefault(),
                    "Initial personalized clip",
                    0f);

                Ensure(frame.environmentId == "japanese_temple_pond_garden", "Generation frame lost the required Japanese-temple environment ID.");
                Ensure(frame.environmentDisplayName == "Japanese Temple Pond Garden", "Generation frame lost the Japanese-temple display context.");

                bool foundEnvironmentPrompt = false;
                foreach (PromptWeight prompt in frame.weightedPrompts)
                {
                    if (prompt.text.Contains("japanese temple"))
                    {
                        foundEnvironmentPrompt = true;
                        break;
                    }
                }

                Ensure(foundEnvironmentPrompt, "Generation frame did not contain Japanese-temple musical guidance.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static void VerifyGeneratedClipStartGate()
        {
            GameObject meditationOwner = new GameObject("MeditationSource_Verification");
            GameObject ambientOwner = new GameObject("AmbientSource_Verification");
            GameObject mixerOwner = new GameObject("AudioMixer_Verification");
            AudioClip rawClip = AudioClip.Create("raw_verification", 512, 1, 44100, false);
            AudioClip generatedClip = AudioClip.Create("generated_verification", 512, 1, 44100, false);
            try
            {
                AudioSource meditationSource = meditationOwner.AddComponent<AudioSource>();
                AudioSource ambientSource = ambientOwner.AddComponent<AudioSource>();
                meditationSource.clip = rawClip;
                ambientSource.clip = rawClip;

                AudioMixerController mixer = mixerOwner.AddComponent<AudioMixerController>();
                SetPrivateField(mixer, "meditationSource", meditationSource);
                SetPrivateField(mixer, "ambientSource", ambientSource);
                mixer.HoldSessionPlayback();
                mixer.ReplaceMeditationClip(generatedClip, false);

                Ensure(mixer.CurrentMeditationClip == generatedClip, "Generated startup clip was not assigned to the meditation source.");
                Ensure(!mixer.IsSessionPlaybackStarted, "Assigning the generated startup clip bypassed the session start gate.");
                Ensure(!mixer.IsMeditationPlaying, "Meditation audio started before the generated-audio gate was released.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mixerOwner);
                UnityEngine.Object.DestroyImmediate(ambientOwner);
                UnityEngine.Object.DestroyImmediate(meditationOwner);
                UnityEngine.Object.DestroyImmediate(generatedClip);
                UnityEngine.Object.DestroyImmediate(rawClip);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Ensure(field != null, $"Verification could not find private field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static AudioRLState BuildState(float stress, float confidence, AudioParameters baseline)
        {
            SignalPacket signal = new SignalPacket(stress, confidence, 0f)
            {
                signalQuality = 1f
            };
            return new AudioRLState
            {
                userId = "verification-user",
                signal = signal,
                currentParameters = baseline,
                personalizedBaseline = baseline,
                preferenceEncoding = new float[AudioRLStateEncoder.PreferenceDimension],
                sessionProgress = 0.25f
            };
        }

        private static AudioParameters BuildParameters(
            float intensity,
            float density,
            float brightness,
            float tempo,
            float fade,
            float musicMix,
            float ambientMix)
        {
            return AudioParameters.FromControlVector(new[]
            {
                intensity,
                density,
                brightness,
                tempo,
                fade,
                musicMix,
                ambientMix
            });
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.001f;
        }

        private static void Ensure(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
