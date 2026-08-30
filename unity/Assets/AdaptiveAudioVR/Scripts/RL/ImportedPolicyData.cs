using System;

namespace AdaptiveAudioVR.RL
{
    [Serializable]
    public class ImportedPolicySample
    {
        public float[] observation;
        public float[] action;
    }

    [Serializable]
    public class ImportedPolicyData
    {
        public string modelId = "imported_policy";
        public string algorithm = "ppo";
        public int seed;
        public int observationDimension;
        public int actionDimension;
        public float maxDelta = 0.08f;
        public int episodeHorizon = 120;
        public int kNeighbors = 8;
        public int exportUserCount;
        public int episodesPerUser;
        public int stepLimit;
        public int sampleCount;
        public string[] observationFeatures;
        public string[] actionFeatures;
        public ImportedPolicySample[] samples;

        public bool IsValid()
        {
            if (samples == null || samples.Length == 0 || observationDimension <= 0 || actionDimension <= 0)
            {
                return false;
            }

            for (int i = 0; i < samples.Length; i++)
            {
                ImportedPolicySample sample = samples[i];
                if (sample == null
                    || sample.observation == null
                    || sample.action == null
                    || sample.observation.Length != observationDimension
                    || sample.action.Length != actionDimension)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
