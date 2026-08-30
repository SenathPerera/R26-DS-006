using System;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "LinUcbPolicyProfile",
        menuName = "Adaptive Meditation/Policies/LinUCB Policy Profile")]
    public sealed class LinUcbPolicyProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("LinUCB Hyperparameters")]
        // TODO(RESEARCH_DECISION): Calibrate and approve ridge and alpha
        // before enabling the contextual-bandit study condition.
        [SerializeField, Min(0f)]
        private float ridgeRegularization = 0f;

        [SerializeField, Min(0f)]
        private float explorationCoefficient = 0f;

        public bool TryCreateRuntimeConfiguration(
            IFeatureVectorBuilder featureVectorBuilder,
            out LinUcbModelConfiguration configuration,
            out string validationError)
        {
            configuration = null;
            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research LinUCB configuration is not approved for runtime use.";
                return false;
            }

            if (featureVectorBuilder == null)
            {
                validationError = "A feature-vector builder is required.";
                return false;
            }

            try
            {
                configuration = new LinUcbModelConfiguration(
                    configurationId,
                    configurationVersion,
                    featureVectorBuilder.FeatureSchemaVersion,
                    featureVectorBuilder.FeatureCount,
                    ridgeRegularization,
                    explorationCoefficient);
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
