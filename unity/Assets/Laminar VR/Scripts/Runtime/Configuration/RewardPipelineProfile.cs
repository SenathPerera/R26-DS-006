using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Rewards;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "RewardPipelineProfile",
        menuName = "Adaptive Meditation/Rewards/Reward Pipeline Profile")]
    public sealed class RewardPipelineProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Baseline and Trend")]
        // TODO(RESEARCH_DECISION): Approve the normalization method, sample
        // counts, variability floor, and trend horizon before runtime use.
        [SerializeField]
        private BaselineStandardDeviationMethod
            baselineStandardDeviationMethod =
                BaselineStandardDeviationMethod.Population;

        [SerializeField, Min(0)]
        private int minimumBaselineSamples = 0;

        [SerializeField, Min(0f)]
        private float minimumBaselineStandardDeviation = 0f;

        [SerializeField, Min(0)]
        private int trendWindowCount = 0;

        [SerializeField, Min(0)]
        private int minimumTrendSamples = 0;

        [Header("Attribution Timing")]
        // TODO(RESEARCH_DECISION): Calibrate against the transition duration,
        // Component B window length, and decision interval.
        [SerializeField, Min(0f)]
        private float settlingSeconds = 0f;

        [SerializeField, Min(0f)]
        private float maximumAttributionWaitSeconds = 0f;

        [Header("Reward Weights")]
        // TODO(RESEARCH_DECISION): Blueprint values are examples, not approved
        // defaults. Keep every weight explicit and versioned.
        [SerializeField, Min(0f)]
        private float stressWeight = 0f;

        [SerializeField, Min(0f)]
        private float rmssdWeight = 0f;

        [SerializeField, Min(0f)]
        private float heartRateWeight = 0f;

        [SerializeField, Min(0f)]
        private float changePenaltyWeight = 0f;

        [SerializeField, Min(0f)]
        private float discomfortPenaltyWeight = 0f;

        [SerializeField, Min(0f)]
        private float safetyPenaltyWeight = 0f;

        public bool TryCreateRuntimeConfiguration(
            out RewardPipelineConfiguration configuration,
            out string validationError)
        {
            configuration = null;
            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research reward configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new RewardPipelineConfiguration(
                    configurationId,
                    configurationVersion,
                    baselineStandardDeviationMethod,
                    minimumBaselineSamples,
                    minimumBaselineStandardDeviation,
                    trendWindowCount,
                    minimumTrendSamples,
                    settlingSeconds,
                    maximumAttributionWaitSeconds,
                    stressWeight,
                    rmssdWeight,
                    heartRateWeight,
                    changePenaltyWeight,
                    discomfortPenaltyWeight,
                    safetyPenaltyWeight);
                validationError = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                validationError = exception.Message;
                return false;
            }
        }
    }
}
