using System.IO;
using UnityEngine;

namespace AdaptiveAudioVR.RL
{
    public class ImportedPolicyRuntime
    {
        private ImportedPolicyData data;

        public bool IsLoaded => data != null && data.IsValid();
        public string ModelId => IsLoaded ? data.modelId : "Unavailable";
        public string Algorithm => IsLoaded ? data.algorithm : "Unavailable";
        public int Seed => IsLoaded ? data.seed : -1;
        public float MaxDelta => IsLoaded ? data.maxDelta : 0.08f;
        public int EpisodeHorizon => IsLoaded ? Mathf.Max(1, data.episodeHorizon) : 120;
        public int ActionDimension => IsLoaded ? data.actionDimension : 0;
        public int SampleCount => IsLoaded ? data.samples.Length : 0;
        public int KNeighbors => IsLoaded ? Mathf.Max(1, data.kNeighbors) : 1;

        public bool TryLoad(string path, out string error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "Imported policy path was empty.";
                    return false;
                }

                if (!File.Exists(path))
                {
                    error = $"Imported policy file was not found at '{path}'.";
                    return false;
                }

                string json = File.ReadAllText(path);
                ImportedPolicyData loaded = JsonUtility.FromJson<ImportedPolicyData>(json);
                if (loaded == null || !loaded.IsValid())
                {
                    error = "Imported policy JSON did not contain a valid sample set.";
                    return false;
                }

                data = loaded;
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public float[] QueryAction(float[] observation, int overrideKNeighbors = -1)
        {
            if (!IsLoaded || observation == null || observation.Length != data.observationDimension)
            {
                return BuildZeroAction();
            }

            int k = Mathf.Clamp(overrideKNeighbors > 0 ? overrideKNeighbors : data.kNeighbors, 1, data.samples.Length);
            float[] bestDistances = new float[k];
            int[] bestIndices = new int[k];

            for (int i = 0; i < k; i++)
            {
                bestDistances[i] = float.MaxValue;
                bestIndices[i] = -1;
            }

            for (int sampleIndex = 0; sampleIndex < data.samples.Length; sampleIndex++)
            {
                ImportedPolicySample sample = data.samples[sampleIndex];
                float distance = 0f;

                for (int dimension = 0; dimension < observation.Length; dimension++)
                {
                    float delta = observation[dimension] - sample.observation[dimension];
                    distance += delta * delta;
                }

                int insertionIndex = -1;
                for (int slot = 0; slot < k; slot++)
                {
                    if (distance < bestDistances[slot])
                    {
                        insertionIndex = slot;
                        break;
                    }
                }

                if (insertionIndex < 0)
                {
                    continue;
                }

                for (int shift = k - 1; shift > insertionIndex; shift--)
                {
                    bestDistances[shift] = bestDistances[shift - 1];
                    bestIndices[shift] = bestIndices[shift - 1];
                }

                bestDistances[insertionIndex] = distance;
                bestIndices[insertionIndex] = sampleIndex;
            }

            float[] weightedAction = BuildZeroAction();
            float totalWeight = 0f;

            for (int i = 0; i < k; i++)
            {
                if (bestIndices[i] < 0)
                {
                    continue;
                }

                float weight = 1f / Mathf.Max(0.0001f, bestDistances[i]);
                float[] action = data.samples[bestIndices[i]].action;
                totalWeight += weight;

                for (int dimension = 0; dimension < data.actionDimension; dimension++)
                {
                    weightedAction[dimension] += action[dimension] * weight;
                }
            }

            if (totalWeight <= 0.0001f)
            {
                return weightedAction;
            }

            for (int dimension = 0; dimension < weightedAction.Length; dimension++)
            {
                weightedAction[dimension] = Mathf.Clamp(weightedAction[dimension] / totalWeight, -1f, 1f);
            }

            return weightedAction;
        }

        public string GetDisplayLabel()
        {
            return IsLoaded
                ? $"{data.algorithm.ToUpperInvariant()} seed {data.seed} sample policy ({data.samples.Length} states)"
                : "No imported policy loaded";
        }

        private float[] BuildZeroAction()
        {
            return new float[IsLoaded ? data.actionDimension : 7];
        }
    }
}
