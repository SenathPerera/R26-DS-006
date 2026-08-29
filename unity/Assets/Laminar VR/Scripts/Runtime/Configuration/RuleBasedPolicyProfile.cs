using System;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "RuleBasedPolicyProfile",
        menuName = "Adaptive Meditation/Policies/Rule-Based Policy Profile")]
    public sealed class RuleBasedPolicyProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Rule Activation")]
        // TODO(RESEARCH_DECISION): Freeze the activation mode and all rule
        // thresholds after pilot review and before the comparative study.
        [SerializeField]
        private RuleActivationMode activationMode =
            RuleActivationMode.WorseningStressTrend;

        [SerializeField, Range(0f, 3f)]
        private float minimumContinuousStressScore = 0f;

        [SerializeField, Min(0f)]
        private float minimumStressIncreasePerMinute = 0f;

        [SerializeField, Range(0f, 1f)]
        private float minimumPreferenceDelta = 0f;

        public bool TryCreateRuntimeConfiguration(
            out RuleBasedPolicyConfiguration configuration,
            out string validationError)
        {
            configuration = null;
            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research rule configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new RuleBasedPolicyConfiguration(
                    configurationId,
                    configurationVersion,
                    activationMode,
                    minimumContinuousStressScore,
                    minimumStressIncreasePerMinute,
                    minimumPreferenceDelta);
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
