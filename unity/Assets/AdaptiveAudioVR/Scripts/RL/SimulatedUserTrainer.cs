using System.IO;
using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.Profile;
using UnityEngine;

namespace AdaptiveAudioVR.RL
{
    public class SimulatedUserTrainer : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private ProfileEngine profileEngine;
        [SerializeField] private RLPersonalizationAgent personalizationAgent;
        [SerializeField] private RLAdaptiveController adaptiveController;

        [Header("Simulated User Preferences")]
        [SerializeField] private UserPreferences simulatedUser = new UserPreferences
        {
            userId = "SimUser_CalmForest",
            mood = "calm",
            tempoPreference = "slow",
            preferredAmbience = "forest",
            preferredInstruments = new[] { "piano", "flute" },
            audioIntensity = 0.25f,
            noveltyTolerance = 0.20f,
            avoidDissonance = true
        };

        [Header("Training Schedule")]
        [SerializeField] private int episodeCount = 200;
        [SerializeField] private int stepsPerEpisode = 120;
        [SerializeField] private float stepDeltaTime = 0.75f;
        [SerializeField] private float disturbanceStrength = 0.08f;

        [Header("Simulated User Response")]
        [SerializeField] private float comfortRecoveryStrength = 0.18f;
        [SerializeField] private float mismatchStressPenalty = 0.14f;
        [SerializeField] private float abruptnessPenalty = 0.10f;
        [SerializeField] private float confidenceGainStrength = 0.10f;
        [SerializeField] private float confidenceLossStrength = 0.08f;

        [Header("Persistence")]
        [SerializeField] private string outputFileName = "trained_rl_model.json";
        [SerializeField] private bool saveModelAfterTraining = true;
        [SerializeField] private bool logTrainingSummary = true;

        [TextArea(4, 10)]
        [SerializeField] private string lastTrainingSummary = "No training run yet.";

        [ContextMenu("Train Simulated User And Save Model")]
        public void TrainSimulatedUserAndSaveModel()
        {
            ResolveReferences();

            if (profileEngine == null || personalizationAgent == null || adaptiveController == null)
            {
                Debug.LogError("[SimulatedUserTrainer] Missing required dependencies.", this);
                return;
            }

            simulatedUser.Normalize();
            AudioProfile profile = profileEngine.GenerateProfile(simulatedUser);
            PersonalizationStrategy strategy = personalizationAgent.SelectStrategy(simulatedUser, profile);
            AudioParameters desiredParameters = strategy.ApplyTo(profile.ToBaselineParameters());

            float totalReward = 0f;
            float finalStress = 0f;
            float finalConfidence = 0f;

            for (int episode = 0; episode < Mathf.Max(1, episodeCount); episode++)
            {
                adaptiveController.Initialize(profile, strategy, resetLearning: episode == 0);

                float stress = Mathf.Lerp(0.65f, 0.35f, episode / Mathf.Max(1f, episodeCount - 1f));
                float confidence = 0.80f;
                SignalPacket signal = new SignalPacket(stress, confidence, 0f);

                for (int step = 0; step < Mathf.Max(1, stepsPerEpisode); step++)
                {
                    AudioParameters parameters = adaptiveController.EvaluateTrainingStep(signal, stepDeltaTime);
                    float alignment = 1f - AverageDistance(parameters, desiredParameters);
                    float abruptness = AverageDistance(parameters, adaptiveController.PersonalizedBaseline);

                    float stressDrift = Random.Range(-disturbanceStrength, disturbanceStrength);
                    stress = Mathf.Clamp01(stress + stressDrift - (alignment * comfortRecoveryStrength) + ((1f - alignment) * mismatchStressPenalty) + (abruptness * abruptnessPenalty));
                    confidence = Mathf.Clamp01(confidence + (alignment * confidenceGainStrength) - ((1f - alignment) * confidenceLossStrength) - (abruptness * 0.05f));

                    signal = new SignalPacket(stress, confidence, (step + 1) * stepDeltaTime);
                    totalReward += adaptiveController.CurrentReward;
                }

                personalizationAgent.UpdateCurrentStrategy(adaptiveController.CurrentReward);
                finalStress = stress;
                finalConfidence = confidence;
            }

            string path = GetOutputPath();
            bool saved = !saveModelAfterTraining || adaptiveController.SaveModelToDisk(path, simulatedUser.userId);
            lastTrainingSummary =
                $"Trained simulated user {simulatedUser.userId}\n" +
                $"Strategy: {personalizationAgent.CurrentStrategyName}\n" +
                $"Episodes: {episodeCount}\n" +
                $"Steps per episode: {stepsPerEpisode}\n" +
                $"Final stress: {finalStress:F3}\n" +
                $"Final confidence: {finalConfidence:F3}\n" +
                $"Accumulated reward: {totalReward:F3}\n" +
                $"Saved model: {saved}\n" +
                $"Output path: {path}";

            if (logTrainingSummary)
            {
                Debug.Log($"[SimulatedUserTrainer] {lastTrainingSummary}", this);
            }
        }

        [ContextMenu("Load Trained Model")]
        public void LoadTrainedModel()
        {
            ResolveReferences();
            if (adaptiveController == null)
            {
                Debug.LogError("[SimulatedUserTrainer] Missing adaptive controller.", this);
                return;
            }

            bool loaded = adaptiveController.LoadModelFromDisk(GetOutputPath());
            Debug.Log(loaded
                ? $"[SimulatedUserTrainer] Loaded trained model from {GetOutputPath()}"
                : $"[SimulatedUserTrainer] No trained model loaded from {GetOutputPath()}", this);
        }

        private string GetOutputPath()
        {
            string folder = Path.Combine(Application.streamingAssetsPath, "Training");
            return Path.Combine(folder, outputFileName);
        }

        private void ResolveReferences()
        {
            profileEngine ??= FindAnyObjectByType<ProfileEngine>();
            personalizationAgent ??= FindAnyObjectByType<RLPersonalizationAgent>();
            adaptiveController ??= FindAnyObjectByType<RLAdaptiveController>();
        }

        private static float AverageDistance(AudioParameters a, AudioParameters b)
        {
            return (Mathf.Abs(a.intensity - b.intensity)
                    + Mathf.Abs(a.density - b.density)
                    + Mathf.Abs(a.brightness - b.brightness)
                    + Mathf.Abs(a.ambientMix - b.ambientMix)
                    + Mathf.Abs(a.musicMix - b.musicMix)) / 5f;
        }
    }
}
