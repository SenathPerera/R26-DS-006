using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "PhysiologyValidationProfile",
        menuName = "Adaptive Meditation/Physiology/Validation Profile")]
    public sealed class PhysiologyValidationProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after Component B compatibility and physiology "
            + "thresholds have been approved for the active pilot or study.")]
        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Window and Freshness Validation")]
        // TODO(RESEARCH_DECISION): These values must be aligned with Component B.
        [SerializeField, Min(0f)]
        private float staleAfterSeconds = 0f;

        [SerializeField, Min(0f)]
        private float minimumWindowDurationSeconds = 0f;

        [SerializeField, Min(0f)]
        private float maximumFutureClockSkewSeconds = 0f;

        [SerializeField, Min(0f)]
        private float sourceTimestampToleranceSeconds = 0f;

        [SerializeField, Range(0f, 1f)]
        private float probabilitySumTolerance = 0f;

        [Header("Signal Quality Gates")]
        // TODO(RESEARCH_DECISION): Do not infer final thresholds from blueprint examples.
        [SerializeField, Range(0f, 1f)]
        private float minimumDecisionSignalQuality = 0f;

        [SerializeField, Range(0f, 1f)]
        private float minimumRewardSignalQuality = 0f;

        [Header("Operational Buffering")]
        [SerializeField, Min(0)]
        private int maximumBufferedWindows = 0;

        public string ConfigurationId => configurationId;

        public int ConfigurationVersion => configurationVersion;

        public bool ResearchConfigurationApproved => researchConfigurationApproved;

        public bool TryCreateRuntimeConfiguration(
            out PhysiologyValidationConfiguration configuration,
            out string validationError)
        {
            configuration = null;

            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research physiology configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new PhysiologyValidationConfiguration(
                    configurationId,
                    configurationVersion,
                    staleAfterSeconds,
                    minimumWindowDurationSeconds,
                    maximumFutureClockSkewSeconds,
                    sourceTimestampToleranceSeconds,
                    probabilitySumTolerance,
                    minimumDecisionSignalQuality,
                    minimumRewardSignalQuality,
                    maximumBufferedWindows);
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

