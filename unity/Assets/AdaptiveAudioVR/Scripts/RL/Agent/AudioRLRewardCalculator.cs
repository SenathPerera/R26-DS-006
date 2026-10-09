using System;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    [Serializable]
    public sealed class AudioRLRewardWeights
    {
        public float stressImprovementWeight = 1.25f;
        public float rmssdImprovementWeight = 0.35f;
        public float heartRateImprovementWeight = 0.15f;
        public float preferenceMatchWeight = 0.45f;
        public float stabilityWeight = 0.30f;
        public float abruptChangePenaltyWeight = 0.55f;
        public float lowConfidenceOverreactionPenaltyWeight = 0.70f;
        public float excessiveNoveltyPenaltyWeight = 0.35f;
        public float unnecessaryInterventionPenaltyWeight = 0.20f;
    }

    [Serializable]
    public sealed class AudioRLRewardBreakdown
    {
        public float totalReward;
        public float stressImprovement;
        public float rmssdImprovement;
        public float heartRateImprovement;
        public float preferenceMatch;
        public float stability;
        public float abruptChangePenalty;
        public float lowConfidenceOverreactionPenalty;
        public float excessiveNoveltyPenalty;
        public float unnecessaryInterventionPenalty;
        public float reliabilityWeight;

        public static AudioRLRewardBreakdown Zero => new AudioRLRewardBreakdown();
    }

    public sealed class AudioRLRewardCalculator
    {
        private readonly AudioRLRewardWeights weights;
        private readonly float maximumActionDelta;

        public AudioRLRewardCalculator(AudioRLRewardWeights weights, float maximumActionDelta)
        {
            this.weights = weights ?? new AudioRLRewardWeights();
            this.maximumActionDelta = Mathf.Max(0.001f, maximumActionDelta);
        }

        public AudioRLRewardBreakdown Compute(
            AudioRLState previousState,
            AudioRLState nextState,
            AudioRLAction finalSafeAction,
            int noveltyCount)
        {
            if (previousState == null || nextState == null)
            {
                return AudioRLRewardBreakdown.Zero;
            }

            float reliability = Mathf.Clamp01(nextState.signal.confidence * nextState.signal.signalQuality);
            float stressImprovement = Mathf.Clamp(previousState.signal.stress - nextState.signal.stress, -1f, 1f);
            float rmssdImprovement = 0f;
            float heartRateImprovement = 0f;

            if (previousState.signal.hasPhysiologyWindow && nextState.signal.hasPhysiologyWindow)
            {
                rmssdImprovement = Mathf.Clamp(
                    (nextState.signal.rmssd - previousState.signal.rmssd) / Mathf.Max(20f, previousState.signal.rmssd),
                    -1f,
                    1f);
                heartRateImprovement = Mathf.Clamp(
                    (previousState.signal.heartRate - nextState.signal.heartRate) / 20f,
                    -1f,
                    1f);
            }

            float preferenceMatch = 1f - MeanDistance(nextState.currentParameters, nextState.personalizedBaseline);
            float normalizedAction = Mathf.Clamp01(finalSafeAction.MeanAbsoluteMagnitude / maximumActionDelta);
            float stability = 1f - normalizedAction;
            float abruptChangePenalty = normalizedAction * normalizedAction;
            float lowConfidencePenalty = normalizedAction * (1f - previousState.signal.confidence);
            float noveltyPenalty = Mathf.Clamp01(noveltyCount / 20f);
            float unnecessaryPenalty = normalizedAction * Mathf.Clamp01((0.40f - previousState.signal.stress) / 0.40f);

            float physiologicalReward = reliability * (
                (weights.stressImprovementWeight * stressImprovement)
                + (weights.rmssdImprovementWeight * rmssdImprovement)
                + (weights.heartRateImprovementWeight * heartRateImprovement));

            float total = physiologicalReward
                          + (weights.preferenceMatchWeight * preferenceMatch)
                          + (weights.stabilityWeight * stability)
                          - (weights.abruptChangePenaltyWeight * abruptChangePenalty)
                          - (weights.lowConfidenceOverreactionPenaltyWeight * lowConfidencePenalty)
                          - (weights.excessiveNoveltyPenaltyWeight * noveltyPenalty)
                          - (weights.unnecessaryInterventionPenaltyWeight * unnecessaryPenalty);

            return new AudioRLRewardBreakdown
            {
                totalReward = Mathf.Clamp(total, -3f, 3f),
                stressImprovement = stressImprovement,
                rmssdImprovement = rmssdImprovement,
                heartRateImprovement = heartRateImprovement,
                preferenceMatch = preferenceMatch,
                stability = stability,
                abruptChangePenalty = abruptChangePenalty,
                lowConfidenceOverreactionPenalty = lowConfidencePenalty,
                excessiveNoveltyPenalty = noveltyPenalty,
                unnecessaryInterventionPenalty = unnecessaryPenalty,
                reliabilityWeight = reliability
            };
        }

        private static float MeanDistance(AudioParameters left, AudioParameters right)
        {
            float[] a = left.ToControlVector();
            float[] b = right.ToControlVector();
            float sum = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                sum += Mathf.Abs(a[i] - b[i]);
            }

            return Mathf.Clamp01(sum / a.Length);
        }
    }
}
