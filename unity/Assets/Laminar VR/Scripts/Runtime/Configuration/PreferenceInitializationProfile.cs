using System;
using LaminarVR.AdaptiveMeditation.Preferences;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "PreferenceInitializationProfile",
        menuName = "Adaptive Meditation/Preferences/Initialization Profile")]
    public sealed class PreferenceInitializationProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after the preference blend has been approved for the "
            + "active pilot or study configuration.")]
        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("First-Session Blend")]
        // TODO(RESEARCH_DECISION): This weight requires pilot/study approval.
        [SerializeField, Range(0f, 1f)]
        private float preferenceWeight = 0f;

        public string ConfigurationId => configurationId;

        public int ConfigurationVersion => configurationVersion;

        public bool ResearchConfigurationApproved => researchConfigurationApproved;

        public bool TryCreateRuntimeConfiguration(
            out PreferenceInitializationConfiguration configuration,
            out string validationError)
        {
            configuration = null;

            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research preference configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new PreferenceInitializationConfiguration(
                    configurationId,
                    configurationVersion,
                    preferenceWeight);
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

