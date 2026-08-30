using System;
using LaminarVR.AdaptiveMeditation.Networking;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "ReconnectBackoffProfile",
        menuName = "Adaptive Meditation/Networking/Reconnect Backoff Profile")]
    public sealed class ReconnectBackoffProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after reconnect behavior is approved for the active "
            + "pilot or study configuration.")]
        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Bounded Backoff")]
        // TODO(RESEARCH_DECISION): Approve attempt count and backoff timing
        // together with stale-data freeze/abort behavior.
        [SerializeField, Min(0)]
        private int maximumAttempts = 0;

        [SerializeField, Min(0f)]
        private float initialDelaySeconds = 0f;

        [SerializeField, Min(0f)]
        private float maximumDelaySeconds = 0f;

        [SerializeField, Min(0f)]
        private float delayMultiplier = 0f;

        public bool TryCreateRuntimeConfiguration(
            out ReconnectBackoffConfiguration configuration,
            out string validationError)
        {
            configuration = null;
            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research reconnect configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new ReconnectBackoffConfiguration(
                    configurationId,
                    configurationVersion,
                    maximumAttempts,
                    initialDelaySeconds,
                    maximumDelaySeconds,
                    delayMultiplier);
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
