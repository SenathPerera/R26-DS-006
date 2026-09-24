using AdaptiveAudioVR.Core;
using System.IO;
using UnityEngine;

namespace AdaptiveAudioVR.RL
{
    public class RLAdaptiveController : MonoBehaviour
    {
        private const string DefaultTrainingFolder = "AdaptiveAudioVR/Training";

        [Header("Decision Timing")]
        [SerializeField] private float decisionIntervalSeconds = 0.75f;
        [SerializeField] private float maxDeltaPerSecond = 0.28f;

        [Header("Q-Learning")]
        [SerializeField] private float learningRate = 0.18f;
        [SerializeField] private float discountFactor = 0.92f;
        [SerializeField] private float epsilon = 0.35f;
        [SerializeField] private float epsilonDecay = 0.995f;
        [SerializeField] private float minEpsilon = 0.05f;

        [Header("Confidence Handling")]
        [SerializeField] private float confidenceFreezeThreshold = 0.25f;
        [SerializeField] private float lowConfidenceThreshold = 0.45f;
        [SerializeField] private float lowConfidenceSpeedMultiplier = 0.35f;
        [SerializeField] private float lowConfidenceBaselineBlend = 0.45f;

        [Header("Action Magnitude")]
        [SerializeField] private float minActionMagnitude = 0.05f;
        [SerializeField] private float maxActionMagnitude = 0.18f;

        [Header("Model Persistence")]
        [SerializeField] private bool loadTrainedModelOnInitialize = false;
        [SerializeField] private string trainedModelFileName = "trained_rl_model.json";
        [SerializeField] private string trainingFolder = DefaultTrainingFolder;

        [Header("Imported Policy")]
        [SerializeField] private bool useImportedPolicy = true;
        [SerializeField] private bool loadImportedPolicyOnInitialize = true;
        [SerializeField] private string importedPolicyFileName = "ppo_seed_37_unity_policy.json";
        [SerializeField] private int importedPolicyKNeighbors = 8;
        [SerializeField] private bool logImportedPolicyLoad = true;

        public AudioProfile ActiveProfile { get; private set; }
        public PersonalizationStrategy ActiveStrategy { get; private set; }
        public AudioParameters CurrentParameters { get; private set; }
        public AudioParameters PersonalizedBaseline { get; private set; }
        public AdaptiveControllerMode CurrentMode { get; private set; } = AdaptiveControllerMode.Initialized;
        public string CurrentActionName { get; private set; } = "Warmup";
        public string CurrentPolicyStatus { get; private set; } = "Bootstrapping";
        public float CurrentReward { get; private set; }
        public bool IsInitialized => ActiveProfile != null;
        public int StateCount => StressBuckets * ConfidenceBuckets * TrendBuckets;
        public int AvailableActionCount => ActionCount;
        public bool UsingImportedPolicy => useImportedPolicy && importedPolicyRuntime.IsLoaded;

        private const int StressBuckets = 3;
        private const int ConfidenceBuckets = 3;
        private const int TrendBuckets = 3;
        private const int ActionCount = 8;
        private const int ImportedObservationDimension = 34;
        private const int ImportedActionDimension = 7;
        private const int ResidualHistoryLength = 3;
        private const int StateIntensity = 0;
        private const int StateDensity = 1;
        private const int StateBrightness = 2;
        private const int StateTempo = 3;
        private const int StateFade = 4;
        private const int StateMusicMix = 5;
        private const int StateAmbientMix = 6;

        private readonly string[] actionNames =
        {
            "Stabilize",
            "Soothe",
            "Activate",
            "Brighten",
            "Darken",
            "Increase Ambient",
            "Increase Music",
            "Novelty Lift"
        };

        private readonly ImportedPolicyRuntime importedPolicyRuntime = new ImportedPolicyRuntime();
        private readonly float[][] recentResidualActions = new float[ResidualHistoryLength][];

        private float[] qValues;
        private AudioParameters currentTarget;
        private SignalPacket previousDecisionSignal;
        private bool hasPreviousDecisionSignal;
        private int previousStateIndex = -1;
        private int previousActionIndex = -1;
        private float nextDecisionTime;

        private int decisionStepCount;
        private int noveltyCount;
        private float baselineTempoState;
        private float baselineFadeState;
        private float currentTempoState;
        private float currentFadeState;

        public void Initialize(AudioProfile profile, PersonalizationStrategy strategy, bool resetLearning = true)
        {
            ActiveProfile = profile;
            ActiveStrategy = strategy ?? PersonalizationStrategy.CreateNeutral();
            PersonalizedBaseline = BuildPersonalizedBaseline();
            CurrentParameters = PersonalizedBaseline;
            currentTarget = CurrentParameters;
            CurrentMode = AdaptiveControllerMode.Initialized;
            CurrentActionName = "Warmup";
            CurrentReward = 0f;
            previousStateIndex = -1;
            previousActionIndex = -1;
            hasPreviousDecisionSignal = false;
            nextDecisionTime = Time.time;
            decisionStepCount = 0;
            noveltyCount = 0;

            baselineTempoState = DeriveTempoBaselineValue(ActiveProfile);
            baselineFadeState = DeriveFadeBaselineValue(ActiveProfile);
            currentTempoState = baselineTempoState;
            currentFadeState = baselineFadeState;
            ResetResidualHistory();

            if (resetLearning || qValues == null || qValues.Length != StateCount * ActionCount)
            {
                InitializeQTable();
            }

            if (loadTrainedModelOnInitialize)
            {
                LoadModelFromDisk();
            }

            if (useImportedPolicy && loadImportedPolicyOnInitialize)
            {
                bool loaded = LoadImportedPolicyFromDisk();
                if (!loaded && logImportedPolicyLoad)
                {
                    Debug.LogWarning($"[RLAdaptiveController] Imported policy '{importedPolicyFileName}' was not loaded. Falling back to local Q-table.", this);
                }
            }

            CurrentPolicyStatus = UsingImportedPolicy
                ? importedPolicyRuntime.GetDisplayLabel()
                : "Bootstrapping";
        }

        public AudioParameters Evaluate(SignalPacket signal, float deltaTime)
        {
            return EvaluateInternal(signal, deltaTime, Time.time >= nextDecisionTime);
        }

        public AudioParameters EvaluateTrainingStep(SignalPacket signal, float deltaTime)
        {
            return EvaluateInternal(signal, deltaTime, true);
        }

        public void ResetLearning()
        {
            InitializeQTable();
            epsilon = Mathf.Max(minEpsilon, epsilon);
            previousStateIndex = -1;
            previousActionIndex = -1;
            hasPreviousDecisionSignal = false;
            CurrentReward = 0f;
            CurrentActionName = "Warmup";
            CurrentPolicyStatus = UsingImportedPolicy ? importedPolicyRuntime.GetDisplayLabel() : "Bootstrapping";
            decisionStepCount = 0;
            noveltyCount = 0;
            ResetResidualHistory();
        }

        public void SyncAppliedParameters(AudioParameters appliedParameters)
        {
            CurrentParameters = appliedParameters.Clamp01();
        }

        public RLAdaptiveModelData ExportModelData(string modelId = "runtime_model")
        {
            return new RLAdaptiveModelData
            {
                modelId = string.IsNullOrWhiteSpace(modelId) ? "runtime_model" : modelId,
                qValues = qValues != null ? (float[])qValues.Clone() : new float[0],
                epsilon = epsilon,
                epsilonDecay = epsilonDecay,
                minEpsilon = minEpsilon,
                learningRate = learningRate,
                discountFactor = discountFactor,
                actionNames = (string[])actionNames.Clone()
            };
        }

        public bool ImportModelData(RLAdaptiveModelData data)
        {
            if (data == null || data.qValues == null || data.qValues.Length != StateCount * ActionCount)
            {
                return false;
            }

            qValues = (float[])data.qValues.Clone();
            epsilon = Mathf.Max(minEpsilon, data.epsilon);
            return true;
        }

        public string GetDefaultModelPath()
        {
            return ResolveTrainingPath(trainedModelFileName);
        }

        public string GetDefaultImportedPolicyPath()
        {
            return ResolveTrainingPath(importedPolicyFileName);
        }

        public bool SaveModelToDisk(string path = null, string modelId = "runtime_model")
        {
            try
            {
                path ??= GetDefaultModelPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(ExportModelData(modelId), true);
                File.WriteAllText(path, json);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RLAdaptiveController] Failed to save model: {ex.Message}", this);
                return false;
            }
        }

        public bool LoadModelFromDisk(string path = null)
        {
            try
            {
                path ??= GetDefaultModelPath();
                if (!File.Exists(path))
                {
                    return false;
                }

                string json = File.ReadAllText(path);
                RLAdaptiveModelData data = JsonUtility.FromJson<RLAdaptiveModelData>(json);
                bool imported = ImportModelData(data);
                if (!imported)
                {
                    Debug.LogWarning("[RLAdaptiveController] Trained model file exists but shape was invalid.", this);
                }

                return imported;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RLAdaptiveController] Failed to load model: {ex.Message}", this);
                return false;
            }
        }

        private string ResolveTrainingPath(string fileName)
        {
            string primaryFolder = Path.Combine(Application.streamingAssetsPath, trainingFolder);
            string primaryPath = Path.Combine(primaryFolder, fileName);
            if (File.Exists(primaryPath))
            {
                return primaryPath;
            }

            string legacyFolder = Path.Combine(Application.streamingAssetsPath, "Training");
            return Path.Combine(legacyFolder, fileName);
        }

        public bool LoadImportedPolicyFromDisk(string path = null)
        {
            path ??= GetDefaultImportedPolicyPath();
            bool loaded = importedPolicyRuntime.TryLoad(path, out string error);

            if (!loaded)
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.LogWarning($"[RLAdaptiveController] Failed to load imported policy: {error}", this);
                }

                return false;
            }

            if (logImportedPolicyLoad)
            {
                Debug.Log($"[RLAdaptiveController] Loaded imported policy {importedPolicyRuntime.GetDisplayLabel()} from {path}.", this);
            }

            return true;
        }

        private AudioParameters EvaluateInternal(SignalPacket signal, float deltaTime, bool allowDecision)
        {
            if (ActiveProfile == null)
            {
                CurrentMode = AdaptiveControllerMode.Initialized;
                return CurrentParameters;
            }

            PersonalizedBaseline = BuildPersonalizedBaseline();
            baselineTempoState = DeriveTempoBaselineValue(ActiveProfile);
            baselineFadeState = DeriveFadeBaselineValue(ActiveProfile);

            if (UsingImportedPolicy)
            {
                return EvaluateImportedPolicy(signal, deltaTime, allowDecision);
            }

            return EvaluateLocalQPolicy(signal, deltaTime, allowDecision);
        }

        private AudioParameters EvaluateImportedPolicy(SignalPacket signal, float deltaTime, bool allowDecision)
        {
            if (allowDecision)
            {
                if (hasPreviousDecisionSignal)
                {
                    CurrentReward = ComputeReward(previousDecisionSignal, signal, CurrentParameters, PersonalizedBaseline, currentTarget);
                }

                float[] observation = BuildImportedObservation(signal);
                float[] residualActionNormalized = importedPolicyRuntime.QueryAction(observation, importedPolicyKNeighbors);
                float[] residualAction = ScaleAction(residualActionNormalized, importedPolicyRuntime.MaxDelta);
                float[] currentState = BuildCurrentStateVector();
                float[] baselineState = BuildBaselineStateVector();
                float[] baselineAction = BuildRuleBaselineAction(signal, currentState, baselineState, importedPolicyRuntime.MaxDelta);
                float[] combinedAction = AddActions(baselineAction, residualAction);
                ImportedSafetyResult safety = ApplyImportedSafety(combinedAction, currentState, baselineState, signal.confidence, importedPolicyRuntime.MaxDelta);

                currentTempoState = safety.safeState[StateTempo];
                currentFadeState = safety.safeState[StateFade];
                currentTarget = MapStateToAudioParameters(safety.safeState, baselineState);
                CurrentActionName = DescribeImportedAction(safety.safeAction, residualAction);
                CurrentPolicyStatus = $"{importedPolicyRuntime.GetDisplayLabel()} | {safety.safetyMode}";
                CurrentMode = DetermineDisplayMode(signal);
                previousDecisionSignal = signal;
                hasPreviousDecisionSignal = true;
                decisionStepCount++;

                if (MeanAbsolute(residualAction) > importedPolicyRuntime.MaxDelta * 0.20f)
                {
                    noveltyCount++;
                }

                StoreResidualAction(residualAction);
                nextDecisionTime = Time.time + Mathf.Max(0.1f, decisionIntervalSeconds);
            }
            else
            {
                CurrentMode = DetermineDisplayMode(signal);
            }

            CurrentParameters = AudioParameters.MoveTowards(CurrentParameters, currentTarget, maxDeltaPerSecond * deltaTime);
            return CurrentParameters.Clamp01();
        }

        private AudioParameters EvaluateLocalQPolicy(SignalPacket signal, float deltaTime, bool allowDecision)
        {
            if (signal.confidence < lowConfidenceThreshold)
            {
                CurrentMode = AdaptiveControllerMode.LowConfidenceDampened;
                CurrentActionName = "Return To Personalized Baseline";
                CurrentPolicyStatus = "Confidence Recovery";
                CurrentReward = 0f;
                currentTarget = AudioParameters.Lerp(currentTarget, PersonalizedBaseline, lowConfidenceBaselineBlend);
                CurrentParameters = AudioParameters.MoveTowards(CurrentParameters, currentTarget, maxDeltaPerSecond * lowConfidenceSpeedMultiplier * deltaTime);
                return CurrentParameters.Clamp01();
            }

            if (allowDecision)
            {
                int stateIndex = EncodeState(signal);

                if (hasPreviousDecisionSignal && previousStateIndex >= 0 && previousActionIndex >= 0)
                {
                    CurrentReward = ComputeReward(previousDecisionSignal, signal, CurrentParameters, PersonalizedBaseline, currentTarget);
                    UpdateQ(previousStateIndex, previousActionIndex, CurrentReward, stateIndex);
                }

                bool exploring = Random.value < Mathf.Lerp(minEpsilon, epsilon, signal.confidence);
                int actionIndex = exploring ? Random.Range(0, ActionCount) : GetBestAction(stateIndex);

                currentTarget = BuildTarget(actionIndex, signal, PersonalizedBaseline);
                previousStateIndex = stateIndex;
                previousActionIndex = actionIndex;
                previousDecisionSignal = signal;
                hasPreviousDecisionSignal = true;

                CurrentActionName = actionNames[actionIndex];
                CurrentPolicyStatus = exploring ? "Exploring" : "Exploiting";
                CurrentMode = DetermineDisplayMode(signal);
                epsilon = Mathf.Max(minEpsilon, epsilon * epsilonDecay);
                nextDecisionTime = Time.time + Mathf.Max(0.1f, decisionIntervalSeconds);
            }
            else
            {
                CurrentMode = DetermineDisplayMode(signal);
            }

            CurrentParameters = AudioParameters.MoveTowards(CurrentParameters, currentTarget, maxDeltaPerSecond * deltaTime);
            return CurrentParameters.Clamp01();
        }

        private AudioParameters BuildPersonalizedBaseline()
        {
            AudioParameters baseline = ActiveProfile != null ? ActiveProfile.ToBaselineParameters() : default;
            return (ActiveStrategy ?? PersonalizationStrategy.CreateNeutral()).ApplyTo(baseline);
        }

        private AudioParameters BuildTarget(int actionIndex, SignalPacket signal, AudioParameters baseline)
        {
            float magnitude = Mathf.Lerp(minActionMagnitude, maxActionMagnitude, signal.confidence);
            float stressBoost = Mathf.InverseLerp(0.5f, 1f, signal.stress);
            AudioParameters target = baseline;

            switch (actionIndex)
            {
                case 0:
                    break;
                case 1:
                    target.intensity -= magnitude;
                    target.density -= magnitude * 0.85f;
                    target.brightness -= magnitude * 0.65f;
                    target.musicMix -= magnitude * 0.55f;
                    target.ambientMix += magnitude * 0.55f;
                    break;
                case 2:
                    target.intensity += magnitude * (1f + (stressBoost * 0.25f));
                    target.density += magnitude * 0.90f;
                    target.brightness += magnitude * 0.70f;
                    target.musicMix += magnitude * 0.65f;
                    target.ambientMix -= magnitude * 0.65f;
                    break;
                case 3:
                    target.brightness += magnitude;
                    target.density += magnitude * 0.30f;
                    target.musicMix += magnitude * 0.10f;
                    break;
                case 4:
                    target.brightness -= magnitude;
                    target.intensity -= magnitude * 0.15f;
                    target.ambientMix += magnitude * 0.20f;
                    break;
                case 5:
                    target.ambientMix += magnitude * 0.80f;
                    target.musicMix -= magnitude * 0.70f;
                    target.density -= magnitude * 0.20f;
                    break;
                case 6:
                    target.musicMix += magnitude * 0.80f;
                    target.ambientMix -= magnitude * 0.70f;
                    target.intensity += magnitude * 0.20f;
                    break;
                case 7:
                    float noveltyScale = Mathf.Lerp(0.2f, 0.85f, ActiveProfile.noveltyTolerance);
                    target.density += magnitude * noveltyScale;
                    target.brightness += magnitude * 0.45f;
                    target.intensity += magnitude * 0.18f;
                    break;
            }

            target = target.Clamp01();
            NormalizeMix(ref target);
            return target;
        }

        private float ComputeReward(SignalPacket previousSignal, SignalPacket currentSignal, AudioParameters parameters, AudioParameters baseline, AudioParameters target)
        {
            float stressReduction = previousSignal.stress - currentSignal.stress;
            float confidenceWeight = Mathf.Lerp(0.25f, 1f, currentSignal.confidence);
            float baselineAlignment = 1f - AverageDistance(parameters, baseline);
            float actionAbruptness = AverageDistance(target, baseline);

            float reward = confidenceWeight * ((stressReduction * 1.5f) + (baselineAlignment * 0.40f))
                           - (actionAbruptness * 0.25f);

            return Mathf.Clamp(reward, -2f, 2f);
        }

        private int EncodeState(SignalPacket signal)
        {
            int stressBucket = signal.stress < 0.35f ? 0 : signal.stress < 0.65f ? 1 : 2;
            int confidenceBucket = signal.confidence < 0.45f ? 0 : signal.confidence < 0.75f ? 1 : 2;

            float trend = hasPreviousDecisionSignal ? signal.stress - previousDecisionSignal.stress : 0f;
            int trendBucket = trend < -0.03f ? 0 : trend > 0.03f ? 2 : 1;

            return stressBucket + (confidenceBucket * StressBuckets) + (trendBucket * StressBuckets * ConfidenceBuckets);
        }

        private void UpdateQ(int stateIndex, int actionIndex, float reward, int nextStateIndex)
        {
            int currentIndex = GetQIndex(stateIndex, actionIndex);
            float currentQ = qValues[currentIndex];
            float nextBestQ = qValues[GetQIndex(nextStateIndex, GetBestAction(nextStateIndex))];
            qValues[currentIndex] = currentQ + (learningRate * (reward + (discountFactor * nextBestQ) - currentQ));
        }

        private int GetBestAction(int stateIndex)
        {
            int bestAction = 0;
            float bestValue = float.MinValue;

            for (int action = 0; action < ActionCount; action++)
            {
                float q = qValues[GetQIndex(stateIndex, action)];
                if (q > bestValue)
                {
                    bestValue = q;
                    bestAction = action;
                }
            }

            return bestAction;
        }

        private void InitializeQTable()
        {
            int stateCount = StressBuckets * ConfidenceBuckets * TrendBuckets;
            qValues = new float[stateCount * ActionCount];

            for (int trend = 0; trend < TrendBuckets; trend++)
            {
                for (int confidence = 0; confidence < ConfidenceBuckets; confidence++)
                {
                    for (int stress = 0; stress < StressBuckets; stress++)
                    {
                        int state = stress + (confidence * StressBuckets) + (trend * StressBuckets * ConfidenceBuckets);

                        SetPrior(state, 0, stress == 1 ? 0.30f : 0.10f);
                        SetPrior(state, 1, stress == 0 || trend == 2 ? 0.35f : 0.05f);
                        SetPrior(state, 2, stress == 2 ? 0.35f : 0.05f);
                        SetPrior(state, 3, stress == 2 ? 0.18f : 0.08f);
                        SetPrior(state, 4, stress == 0 ? 0.20f : 0.06f);
                        SetPrior(state, 5, stress == 0 ? 0.32f : 0.08f);
                        SetPrior(state, 6, stress == 2 ? 0.25f : 0.08f);
                        SetPrior(state, 7, confidence == 2 ? 0.10f : 0.02f);

                        if (confidence == 0)
                        {
                            SetPrior(state, 0, qValues[GetQIndex(state, 0)] + 0.12f);
                            SetPrior(state, 5, qValues[GetQIndex(state, 5)] + 0.05f);
                        }
                    }
                }
            }
        }

        private void SetPrior(int stateIndex, int actionIndex, float value)
        {
            qValues[GetQIndex(stateIndex, actionIndex)] = value;
        }

        private int GetQIndex(int stateIndex, int actionIndex)
        {
            return (stateIndex * ActionCount) + actionIndex;
        }

        private float[] BuildImportedObservation(SignalPacket signal)
        {
            float[] observation = new float[ImportedObservationDimension];
            float[] preferenceVector = BuildImportedPreferenceVector();
            float[] currentState = BuildCurrentStateVector();
            float[] meanResidualAction = BuildMeanResidualAction();
            float stressTrend = hasPreviousDecisionSignal ? signal.stress - previousDecisionSignal.stress : 0f;
            float confidenceTrend = hasPreviousDecisionSignal ? signal.confidence - previousDecisionSignal.confidence : 0f;
            float maxDelta = Mathf.Max(0.0001f, importedPolicyRuntime.MaxDelta);

            int index = 0;

            for (int i = 0; i < preferenceVector.Length; i++)
            {
                observation[index++] = preferenceVector[i];
            }

            for (int i = 0; i < currentState.Length; i++)
            {
                observation[index++] = currentState[i];
            }

            observation[index++] = Mathf.Clamp01(signal.stress);
            observation[index++] = Mathf.Clamp01(signal.confidence);
            observation[index++] = Mathf.Clamp01(stressTrend + 0.5f);
            observation[index++] = Mathf.Clamp01(confidenceTrend + 0.5f);

            for (int i = 0; i < meanResidualAction.Length; i++)
            {
                observation[index++] = Mathf.Clamp01(meanResidualAction[i] + 0.5f);
            }

            observation[index++] = Mathf.Clamp01(decisionStepCount / (float)Mathf.Max(1, importedPolicyRuntime.EpisodeHorizon));
            observation[index++] = Mathf.Clamp01(noveltyCount / 20f);
            observation[index] = Mathf.Clamp01(MeanAbsolute(meanResidualAction) / maxDelta);

            return observation;
        }

        private float[] BuildImportedPreferenceVector()
        {
            AudioParameters baseline = PersonalizedBaseline;
            return new[]
            {
                baseline.intensity,
                baseline.density,
                baseline.brightness,
                baselineTempoState,
                baseline.musicMix,
                baseline.ambientMix,
                DeriveRhythmAmount(ActiveProfile, baselineTempoState, baseline.density),
                DeriveNatureLevel(ActiveProfile),
                DeriveReverbAmount(ActiveProfile),
                Mathf.Clamp01(ActiveProfile != null ? ActiveProfile.noveltyTolerance : 0.2f),
                ActiveProfile != null && ActiveProfile.avoidDissonance ? 0.15f : 0.45f,
                DeriveRelaxationResponsiveness(ActiveProfile),
                DeriveConfidenceSensitivity(ActiveProfile)
            };
        }

        private float[] BuildCurrentStateVector()
        {
            return new[]
            {
                CurrentParameters.intensity,
                CurrentParameters.density,
                CurrentParameters.brightness,
                currentTempoState,
                currentFadeState,
                CurrentParameters.musicMix,
                CurrentParameters.ambientMix
            };
        }

        private float[] BuildBaselineStateVector()
        {
            return new[]
            {
                PersonalizedBaseline.intensity,
                PersonalizedBaseline.density,
                PersonalizedBaseline.brightness,
                baselineTempoState,
                baselineFadeState,
                PersonalizedBaseline.musicMix,
                PersonalizedBaseline.ambientMix
            };
        }

        private float[] BuildRuleBaselineAction(SignalPacket signal, float[] currentState, float[] baselineState, float maxDelta)
        {
            float[] target = (float[])baselineState.Clone();
            float stress = Mathf.Clamp01(signal.stress);
            float confidence = Mathf.Clamp01(signal.confidence);

            if (confidence < lowConfidenceThreshold)
            {
                for (int i = 0; i < target.Length; i++)
                {
                    target[i] = Mathf.Lerp(currentState[i], baselineState[i], 0.55f);
                }
            }
            else if (stress > 0.65f)
            {
                target[StateIntensity] = Mathf.Clamp01(baselineState[StateIntensity] + 0.10f);
                target[StateDensity] = Mathf.Clamp01(baselineState[StateDensity] + 0.08f);
                target[StateBrightness] = Mathf.Clamp01(baselineState[StateBrightness] + 0.05f);
                target[StateMusicMix] = Mathf.Clamp01(baselineState[StateMusicMix] + 0.10f);
                target[StateAmbientMix] = Mathf.Clamp01(baselineState[StateAmbientMix] - 0.10f);
            }
            else if (stress < 0.35f)
            {
                target[StateIntensity] = Mathf.Clamp01(baselineState[StateIntensity] - 0.08f);
                target[StateDensity] = Mathf.Clamp01(baselineState[StateDensity] - 0.06f);
                target[StateBrightness] = Mathf.Clamp01(baselineState[StateBrightness] - 0.04f);
                target[StateMusicMix] = Mathf.Clamp01(baselineState[StateMusicMix] - 0.08f);
                target[StateAmbientMix] = Mathf.Clamp01(baselineState[StateAmbientMix] + 0.08f);
            }

            NormalizeMix(target);
            float[] delta = new float[ImportedActionDimension];
            for (int i = 0; i < delta.Length; i++)
            {
                delta[i] = Mathf.Clamp(target[i] - currentState[i], -maxDelta, maxDelta);
            }

            return delta;
        }

        private ImportedSafetyResult ApplyImportedSafety(float[] proposedAction, float[] currentState, float[] baselineState, float confidence, float maxDelta)
        {
            string safetyMode = "Normal";
            float[] safeAction = new float[ImportedActionDimension];

            for (int i = 0; i < safeAction.Length; i++)
            {
                safeAction[i] = Mathf.Clamp(proposedAction[i], -maxDelta, maxDelta);
            }

            if (confidence < confidenceFreezeThreshold)
            {
                safetyMode = "ConfidenceFreeze";
                for (int i = 0; i < safeAction.Length; i++)
                {
                    safeAction[i] = 0f;
                }
            }
            else if (confidence < lowConfidenceThreshold)
            {
                safetyMode = "LowConfidenceDampened";
                float damp = Mathf.Lerp(0.15f, 0.50f, Mathf.InverseLerp(confidenceFreezeThreshold, lowConfidenceThreshold, confidence));
                for (int i = 0; i < safeAction.Length; i++)
                {
                    safeAction[i] *= damp;
                }
            }

            float[] safeState = new float[ImportedActionDimension];
            for (int i = 0; i < safeState.Length; i++)
            {
                safeState[i] = Mathf.Clamp01(currentState[i] + safeAction[i]);
            }

            NormalizeMix(safeState);
            float baselineDelta = MeanAbsoluteDifference(safeState, baselineState);
            if (baselineDelta > 0.30f)
            {
                safetyMode = "BaselineRecovery";
                for (int i = 0; i < safeState.Length; i++)
                {
                    safeState[i] = Mathf.Lerp(baselineState[i], safeState[i], 0.5f);
                }

                NormalizeMix(safeState);
            }

            for (int i = 0; i < safeAction.Length; i++)
            {
                safeAction[i] = safeState[i] - currentState[i];
            }

            return new ImportedSafetyResult
            {
                safeAction = safeAction,
                safeState = safeState,
                safetyMode = safetyMode
            };
        }

        private AudioParameters MapStateToAudioParameters(float[] safeState, float[] baselineState)
        {
            AudioParameters mapped = new AudioParameters
            {
                intensity = safeState[StateIntensity],
                density = safeState[StateDensity],
                brightness = safeState[StateBrightness],
                tempo = safeState[StateTempo],
                fade = safeState[StateFade],
                musicMix = safeState[StateMusicMix],
                ambientMix = safeState[StateAmbientMix]
            };

            float tempoDelta = safeState[StateTempo] - baselineState[StateTempo];
            float fadeDelta = safeState[StateFade] - baselineState[StateFade];

            mapped.intensity = Mathf.Clamp01(mapped.intensity + (tempoDelta * 0.18f));
            mapped.density = Mathf.Clamp01(mapped.density + (tempoDelta * 0.28f));
            mapped.ambientMix = Mathf.Clamp01(mapped.ambientMix + (fadeDelta * 0.12f));
            mapped.musicMix = Mathf.Clamp01(mapped.musicMix - (fadeDelta * 0.12f));
            NormalizeMix(ref mapped);
            return mapped.Clamp01();
        }

        private string DescribeImportedAction(float[] safeAction, float[] residualAction)
        {
            if (MeanAbsolute(safeAction) <= 0.004f)
            {
                return "Imported Stabilize";
            }

            int dominantIndex = 0;
            float dominantValue = 0f;
            for (int i = 0; i < safeAction.Length; i++)
            {
                float value = Mathf.Abs(safeAction[i]);
                if (value > dominantValue)
                {
                    dominantValue = value;
                    dominantIndex = i;
                }
            }

            switch (dominantIndex)
            {
                case StateMusicMix:
                    return safeAction[dominantIndex] >= 0f ? "Imported Increase Music" : "Imported Reduce Music";
                case StateAmbientMix:
                    return safeAction[dominantIndex] >= 0f ? "Imported Increase Ambient" : "Imported Reduce Ambient";
                case StateBrightness:
                    return safeAction[dominantIndex] >= 0f ? "Imported Brighten" : "Imported Darken";
                case StateIntensity:
                    return safeAction[dominantIndex] >= 0f ? "Imported Activate" : "Imported Soothe";
                case StateDensity:
                    return safeAction[dominantIndex] >= 0f ? "Imported Densify" : "Imported Soften Texture";
                case StateTempo:
                    return residualAction[dominantIndex] >= 0f ? "Imported Tempo Lift" : "Imported Tempo Ease";
                case StateFade:
                    return residualAction[dominantIndex] >= 0f ? "Imported Fade Ease" : "Imported Fade Tighten";
                default:
                    return "Imported Residual Blend";
            }
        }

        private void ResetResidualHistory()
        {
            for (int i = 0; i < recentResidualActions.Length; i++)
            {
                recentResidualActions[i] = new float[ImportedActionDimension];
            }
        }

        private void StoreResidualAction(float[] residualAction)
        {
            for (int i = 0; i < recentResidualActions.Length - 1; i++)
            {
                recentResidualActions[i] = recentResidualActions[i + 1];
            }

            recentResidualActions[recentResidualActions.Length - 1] = (float[])residualAction.Clone();
        }

        private float[] BuildMeanResidualAction()
        {
            float[] mean = new float[ImportedActionDimension];
            for (int i = 0; i < recentResidualActions.Length; i++)
            {
                float[] action = recentResidualActions[i];
                if (action == null)
                {
                    continue;
                }

                for (int dimension = 0; dimension < ImportedActionDimension; dimension++)
                {
                    mean[dimension] += action[dimension];
                }
            }

            for (int dimension = 0; dimension < ImportedActionDimension; dimension++)
            {
                mean[dimension] /= Mathf.Max(1, recentResidualActions.Length);
            }

            return mean;
        }

        private static float[] ScaleAction(float[] normalizedAction, float scale)
        {
            float[] scaled = new float[ImportedActionDimension];
            int count = Mathf.Min(normalizedAction != null ? normalizedAction.Length : 0, ImportedActionDimension);
            for (int i = 0; i < count; i++)
            {
                scaled[i] = Mathf.Clamp(normalizedAction[i], -1f, 1f) * scale;
            }

            return scaled;
        }

        private static float[] AddActions(float[] a, float[] b)
        {
            float[] combined = new float[ImportedActionDimension];
            for (int i = 0; i < combined.Length; i++)
            {
                combined[i] = (a != null ? a[i] : 0f) + (b != null ? b[i] : 0f);
            }

            return combined;
        }

        private static void NormalizeMix(float[] state)
        {
            float total = state[StateMusicMix] + state[StateAmbientMix];
            if (total <= 0.001f)
            {
                state[StateMusicMix] = 0.5f;
                state[StateAmbientMix] = 0.5f;
                return;
            }

            state[StateMusicMix] /= total;
            state[StateAmbientMix] /= total;
        }

        private static float MeanAbsolute(float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                total += Mathf.Abs(values[i]);
            }

            return total / values.Length;
        }

        private static float MeanAbsoluteDifference(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0)
            {
                return 0f;
            }

            int count = Mathf.Min(a.Length, b.Length);
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                total += Mathf.Abs(a[i] - b[i]);
            }

            return total / count;
        }

        private static float DeriveTempoBaselineValue(AudioProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.tempo))
            {
                return 0.2f;
            }

            switch (profile.tempo)
            {
                case "fast":
                    return 0.8f;
                case "medium":
                    return 0.5f;
                default:
                    return 0.2f;
            }
        }

        private static float DeriveFadeBaselineValue(AudioProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.mood))
            {
                return 0.55f;
            }

            switch (profile.mood)
            {
                case "sleepy":
                    return 0.82f;
                case "calm":
                    return 0.70f;
                case "focused":
                    return 0.45f;
                case "energized":
                    return 0.30f;
                default:
                    return 0.55f;
            }
        }

        private static float DeriveNatureLevel(AudioProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.ambience))
            {
                return 0.4f;
            }

            switch (profile.ambience)
            {
                case "forest":
                    return 0.90f;
                case "ocean":
                    return 0.75f;
                case "rain":
                    return 0.80f;
                case "temple":
                    return 0.20f;
                case "studio":
                    return 0.05f;
                default:
                    return 0.35f;
            }
        }

        private static float DeriveReverbAmount(AudioProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.ambience))
            {
                return 0.5f;
            }

            switch (profile.ambience)
            {
                case "forest":
                    return 0.55f;
                case "ocean":
                    return 0.70f;
                case "rain":
                    return 0.60f;
                case "temple":
                    return 0.80f;
                case "studio":
                    return 0.35f;
                default:
                    return 0.5f;
            }
        }

        private static float DeriveRhythmAmount(AudioProfile profile, float tempoValue, float densityValue)
        {
            float rhythm = (tempoValue * 0.55f) + (densityValue * 0.45f);
            if (profile != null && profile.instruments != null)
            {
                for (int i = 0; i < profile.instruments.Length; i++)
                {
                    string instrument = profile.instruments[i];
                    if (instrument == "drums" || instrument == "percussion")
                    {
                        rhythm += 0.12f;
                    }
                }
            }

            return Mathf.Clamp01(rhythm);
        }

        private static float DeriveRelaxationResponsiveness(AudioProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.mood))
            {
                return 0.95f;
            }

            switch (profile.mood)
            {
                case "sleepy":
                    return 1.15f;
                case "calm":
                    return 1.05f;
                case "focused":
                    return 0.90f;
                case "energized":
                    return 0.82f;
                default:
                    return 0.95f;
            }
        }

        private static float DeriveConfidenceSensitivity(AudioProfile profile)
        {
            if (profile == null)
            {
                return 1.0f;
            }

            return Mathf.Lerp(1.18f, 0.88f, profile.noveltyTolerance);
        }

        private static float AverageDistance(AudioParameters a, AudioParameters b)
        {
            return (Mathf.Abs(a.intensity - b.intensity)
                    + Mathf.Abs(a.density - b.density)
                    + Mathf.Abs(a.brightness - b.brightness)
                    + Mathf.Abs(a.ambientMix - b.ambientMix)
                    + Mathf.Abs(a.musicMix - b.musicMix)) / 5f;
        }

        private static void NormalizeMix(ref AudioParameters parameters)
        {
            float total = parameters.ambientMix + parameters.musicMix;
            if (total <= 0.001f)
            {
                parameters.ambientMix = 0.5f;
                parameters.musicMix = 0.5f;
                return;
            }

            parameters.ambientMix /= total;
            parameters.musicMix /= total;
        }

        private AdaptiveControllerMode DetermineDisplayMode(SignalPacket signal)
        {
            if (signal.confidence < lowConfidenceThreshold)
            {
                return AdaptiveControllerMode.LowConfidenceDampened;
            }

            if (signal.stress >= 0.65f)
            {
                return AdaptiveControllerMode.HighStressAdaptive;
            }

            if (signal.stress <= 0.35f)
            {
                return AdaptiveControllerMode.LowStressCalming;
            }

            return AdaptiveControllerMode.MidRangeStabilizing;
        }

        private struct ImportedSafetyResult
        {
            public float[] safeAction;
            public float[] safeState;
            public string safetyMode;
        }
    }
}
