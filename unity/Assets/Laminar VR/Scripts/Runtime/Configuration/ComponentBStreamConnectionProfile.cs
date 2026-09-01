using System;
using LaminarVR.AdaptiveMeditation.Networking;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "ComponentBStreamConnectionProfile",
        menuName = "Adaptive Meditation/Networking/Component B Stream Profile")]
    public sealed class ComponentBStreamConnectionProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after the endpoint and operational limits are valid "
            + "for the active deployment environment.")]
        [SerializeField]
        private bool deploymentConfigurationApproved = false;

        [Header("Component B Prediction Stream")]
        [Tooltip(
            "Use ws:// for controlled local development and wss:// for a "
            + "deployed build. Quest cannot reach a PC server through localhost.")]
        [SerializeField]
        private string streamEndpoint = string.Empty;

        [SerializeField, Min(0f)]
        private float keepaliveIntervalSeconds = 0f;

        [SerializeField, Min(0)]
        private int maximumMessageBytes = 0;

        public bool TryCreateRuntimeConfiguration(
            out ComponentBStreamConnectionConfiguration configuration,
            out string validationError)
        {
            configuration = null;
            if (!deploymentConfigurationApproved)
            {
                validationError =
                    "Component B deployment configuration is not approved.";
                return false;
            }

            try
            {
                configuration = new ComponentBStreamConnectionConfiguration(
                    configurationId,
                    configurationVersion,
                    streamEndpoint,
                    keepaliveIntervalSeconds,
                    maximumMessageBytes);
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
