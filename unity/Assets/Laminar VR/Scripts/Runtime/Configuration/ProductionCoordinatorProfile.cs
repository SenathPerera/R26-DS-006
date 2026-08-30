using System;
using LaminarVR.AdaptiveMeditation.Application;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "ProductionCoordinatorProfile",
        menuName = "Adaptive Meditation/Application/Production Coordinator Profile")]
    public sealed class ProductionCoordinatorProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after the cadence and session-level safety limits "
            + "have been approved for the active pilot or study.")]
        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Component B Output Cadence")]
        [Tooltip(
            "Expected seconds between Component B stress outputs. This is "
            + "separate from frame-based visual interpolation.")]
        // Component B currently emits one output every 60 seconds. Keep this
        // configurable so a future contract revision does not require code changes.
        [SerializeField, Min(0f)]
        private float expectedPhysiologyOutputIntervalSeconds = 60f;

        [Header("Session-level Safety Limits")]
        // TODO(RESEARCH_DECISION): Approve both limits before pilot use.
        [SerializeField, Min(0)]
        private int maximumConsecutiveSameDirectionActions = 0;

        [SerializeField, Min(0f)]
        private float maximumTotalVariation = 0f;

        public bool TryCreateRuntimeConfiguration(
            out ProductionCoordinatorConfiguration configuration,
            out string validationError)
        {
            configuration = null;
            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research coordinator configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                configuration = new ProductionCoordinatorConfiguration(
                    configurationId,
                    configurationVersion,
                    expectedPhysiologyOutputIntervalSeconds,
                    maximumConsecutiveSameDirectionActions,
                    maximumTotalVariation);
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
