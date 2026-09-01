using System;
using LaminarVR.AdaptiveMeditation.Telemetry;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "TelemetryLoggingProfile",
        menuName = "Adaptive Meditation/Telemetry/Logging Profile")]
    public sealed class TelemetryLoggingProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after telemetry schema and local logging behavior "
            + "have been approved for the active pilot or study.")]
        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Event Schema")]
        // TODO(RESEARCH_DECISION): Approve the telemetry schema identity and
        // version before using logs as pilot or study data.
        [SerializeField]
        private string eventSchemaId = string.Empty;

        [SerializeField]
        private string eventSchemaVersion = string.Empty;

        [Header("Local JSON Lines")]
        // TODO(RESEARCH_DECISION): Approve flush cadence together with storage,
        // retention, recovery, and export requirements.
        [SerializeField, Min(0)]
        private int flushEveryEventCount = 0;

        public bool TryCreateRuntimeConfiguration(
            out TelemetryLoggingConfiguration configuration,
            out string validationError)
        {
            configuration = null;
            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research telemetry configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new TelemetryLoggingConfiguration(
                    configurationId,
                    configurationVersion,
                    eventSchemaId,
                    eventSchemaVersion,
                    flushEveryEventCount);
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
