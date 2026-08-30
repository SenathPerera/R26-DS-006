using System;
using LaminarVR.AdaptiveMeditation.Session;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "SessionTimingProfile",
        menuName = "Adaptive Meditation/Session/Timing Profile")]
    public sealed class SessionTimingProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after the timing values have been approved for the "
            + "active pilot or study configuration.")]
        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Phase Timing (seconds)")]
        // TODO(RESEARCH_DECISION): Zero keeps new timing profiles invalid until calibrated.
        [SerializeField, Min(0f)]
        private float acclimatizationDurationSeconds = 0f;

        [SerializeField, Min(0f)]
        private float adaptiveDurationSeconds = 0f;

        [SerializeField, Min(0f)]
        private float stabilizationDurationSeconds = 0f;

        [Header("Decision Schedule (seconds)")]
        // TODO(RESEARCH_DECISION): Coordinate this with transition and physiology windows.
        [SerializeField, Min(0f)]
        private float decisionIntervalSeconds = 0f;

        public string ConfigurationId => configurationId;

        public int ConfigurationVersion => configurationVersion;

        public bool ResearchConfigurationApproved => researchConfigurationApproved;

        public bool TryCreateRuntimeConfiguration(
            out SessionTimingConfiguration configuration,
            out string validationError)
        {
            configuration = null;

            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research timing configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new SessionTimingConfiguration(
                    configurationId,
                    configurationVersion,
                    acclimatizationDurationSeconds,
                    adaptiveDurationSeconds,
                    stabilizationDurationSeconds,
                    decisionIntervalSeconds);
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

