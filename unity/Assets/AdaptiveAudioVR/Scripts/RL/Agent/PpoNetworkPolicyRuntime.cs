using System;
using System.IO;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    [Serializable]
    public sealed class PpoDenseLayerData
    {
        public int inputSize;
        public int outputSize;
        public float[] weights;
        public float[] biases;

        public bool IsValid()
        {
            return inputSize > 0
                   && outputSize > 0
                   && weights != null
                   && weights.Length == inputSize * outputSize
                   && biases != null
                   && biases.Length == outputSize;
        }
    }

    [Serializable]
    public sealed class PpoNetworkPolicyData
    {
        public string modelId;
        public string algorithm;
        public int seed;
        public int observationDimension;
        public int actionDimension;
        public float maxDelta;
        public int episodeHorizon;
        public string activation;
        public string sourceModelSha256;
        public PpoDenseLayerData[] layers;
        public FloatArrayData[] verificationObservations;
        public FloatArrayData[] verificationActions;

        public bool IsValid()
        {
            if (observationDimension != AudioRLStateEncoder.ObservationDimension
                || actionDimension != 7
                || !string.Equals(algorithm, "ppo", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(activation, "tanh", StringComparison.OrdinalIgnoreCase)
                || maxDelta <= 0f
                || maxDelta > 1f
                || layers == null
                || layers.Length != 3)
            {
                return false;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null || !layers[i].IsValid())
                {
                    return false;
                }

                if (i > 0 && layers[i - 1].outputSize != layers[i].inputSize)
                {
                    return false;
                }
            }

            return layers[0].inputSize == observationDimension
                   && layers[layers.Length - 1].outputSize == actionDimension;
        }
    }

    [Serializable]
    public sealed class FloatArrayData
    {
        public float[] values;
    }

    public sealed class PpoNetworkPolicyRuntime
    {
        private PpoNetworkPolicyData data;

        public bool IsLoaded => data != null && data.IsValid();
        public string ModelId => IsLoaded ? data.modelId : "Unavailable";
        public int Seed => IsLoaded ? data.seed : -1;
        public float MaximumDelta => IsLoaded ? data.maxDelta : 0.08f;
        public int EpisodeHorizon => IsLoaded ? data.episodeHorizon : 120;
        public string SourceModelSha256 => IsLoaded ? data.sourceModelSha256 : string.Empty;

        public bool TryLoad(string path, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    error = $"Direct PPO network file was not found at '{path}'.";
                    return false;
                }

                PpoNetworkPolicyData loaded = JsonUtility.FromJson<PpoNetworkPolicyData>(File.ReadAllText(path));
                if (loaded == null || !loaded.IsValid())
                {
                    error = "Direct PPO network JSON was invalid or incompatible with the 34x7 policy contract.";
                    return false;
                }

                data = loaded;
                if (!ValidateEmbeddedExamples(out error))
                {
                    data = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                data = null;
                return false;
            }
        }

        public float[] QueryAction(float[] observation)
        {
            if (!IsLoaded || observation == null || observation.Length != data.observationDimension)
            {
                return new float[7];
            }

            float[] values = (float[])observation.Clone();
            for (int layerIndex = 0; layerIndex < data.layers.Length; layerIndex++)
            {
                values = EvaluateLayer(values, data.layers[layerIndex], layerIndex < data.layers.Length - 1);
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Mathf.Clamp(values[i], -1f, 1f);
            }

            return values;
        }

        private bool ValidateEmbeddedExamples(out string error)
        {
            error = null;
            if (data.verificationObservations == null
                || data.verificationActions == null
                || data.verificationObservations.Length == 0
                || data.verificationObservations.Length != data.verificationActions.Length)
            {
                error = "Direct PPO network did not contain export verification examples.";
                return false;
            }

            for (int sample = 0; sample < data.verificationObservations.Length; sample++)
            {
                float[] observation = data.verificationObservations[sample]?.values;
                float[] expected = data.verificationActions[sample]?.values;
                if (observation == null
                    || observation.Length != data.observationDimension
                    || expected == null
                    || expected.Length != data.actionDimension)
                {
                    error = $"Direct PPO verification example {sample} was malformed.";
                    return false;
                }

                float[] actual = QueryActionUnchecked(observation);
                for (int i = 0; i < expected.Length; i++)
                {
                    if (Mathf.Abs(actual[i] - expected[i]) > 0.0001f)
                    {
                        error = $"Direct PPO verification failed at sample {sample}, action {i}.";
                        return false;
                    }
                }
            }

            return true;
        }

        private float[] QueryActionUnchecked(float[] observation)
        {
            float[] values = (float[])observation.Clone();
            for (int layerIndex = 0; layerIndex < data.layers.Length; layerIndex++)
            {
                values = EvaluateLayer(values, data.layers[layerIndex], layerIndex < data.layers.Length - 1);
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Mathf.Clamp(values[i], -1f, 1f);
            }

            return values;
        }

        private static float[] EvaluateLayer(float[] input, PpoDenseLayerData layer, bool applyTanh)
        {
            float[] output = new float[layer.outputSize];
            for (int outputIndex = 0; outputIndex < layer.outputSize; outputIndex++)
            {
                float value = layer.biases[outputIndex];
                int weightOffset = outputIndex * layer.inputSize;
                for (int inputIndex = 0; inputIndex < layer.inputSize; inputIndex++)
                {
                    value += input[inputIndex] * layer.weights[weightOffset + inputIndex];
                }

                output[outputIndex] = applyTanh ? (float)Math.Tanh(value) : value;
            }

            return output;
        }
    }
}
