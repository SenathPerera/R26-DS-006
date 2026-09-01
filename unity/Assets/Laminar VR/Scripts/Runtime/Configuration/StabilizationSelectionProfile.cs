using System;
using LaminarVR.AdaptiveMeditation.Stabilization;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "StabilizationSelectionProfile",
        menuName = "Adaptive Meditation/Stabilization/Selection Profile")]
    public sealed class StabilizationSelectionProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Best Recent State")]
        // TODO(RESEARCH_DECISION): Freeze these values before pilot use. The
        // blueprint's example range is guidance, not an approved default.
        [SerializeField, Min(0)]
        private int recentOutcomeCount = 0;

        [SerializeField, Range(0f, 1f)]
        private float rewardRecencyDecay = 0f;

        [SerializeField, Min(0f)]
        private float preferenceDistancePenaltyWeight = 0f;

        public bool TryCreateRuntimeConfiguration(
            out StabilizationSelectionConfiguration configuration,
            out string validationError)
        {
            configuration = null;
            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research stabilization configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new StabilizationSelectionConfiguration(
                    configurationId,
                    configurationVersion,
                    recentOutcomeCount,
                    rewardRecencyDecay,
                    preferenceDistancePenaltyWeight);
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
