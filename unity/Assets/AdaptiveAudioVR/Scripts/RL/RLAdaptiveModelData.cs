using System;
using UnityEngine;

namespace AdaptiveAudioVR.RL
{
    [Serializable]
    public class RLAdaptiveModelData
    {
        public string modelId = "runtime_model";
        public float[] qValues;
        public float epsilon;
        public float epsilonDecay;
        public float minEpsilon;
        public float learningRate;
        public float discountFactor;
        public string[] actionNames;
    }
}
