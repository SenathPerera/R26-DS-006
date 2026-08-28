using System;
using LaminarVR.AdaptiveMeditation.Environment;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "SceneParameterProfile",
        menuName = "Adaptive Meditation/Environment/Scene Parameter Profile")]
    public sealed class SceneParameterProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string sceneId = string.Empty;

        [SerializeField]
        private string displayName = string.Empty;

        [Tooltip(
            "Enable only after the scene ranges, action step, and timings have "
            + "been approved for the active pilot or study configuration.")]
        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Safe Normalized Default")]
        // TODO(RESEARCH_DECISION): The neutral values below are authoring placeholders only.
        [SerializeField, Range(0f, 1f)]
        private float defaultIllumination = 0.5f;

        [SerializeField, Range(0f, 1f)]
        private float defaultWarmth = 0.5f;

        [SerializeField, Range(0f, 1f)]
        private float defaultAtmosphericSoftness = 0.5f;

        [SerializeField, Range(0f, 1f)]
        private float defaultColorRichness = 0.5f;

        [SerializeField, Range(0f, 1f)]
        private float defaultAmbientMotion = 0.5f;

        [Header("Allowed Normalized Ranges")]
        // TODO(RESEARCH_DECISION): Full ranges are not validated scene-safe ranges.
        [SerializeField]
        private Vector2 illuminationRange = new Vector2(0f, 1f);

        [SerializeField]
        private Vector2 warmthRange = new Vector2(0f, 1f);

        [SerializeField]
        private Vector2 atmosphericSoftnessRange = new Vector2(0f, 1f);

        [SerializeField]
        private Vector2 colorRichnessRange = new Vector2(0f, 1f);

        [SerializeField]
        private Vector2 ambientMotionRange = new Vector2(0f, 1f);

        [Header("Action and Transition Limits")]
        // TODO(RESEARCH_DECISION): Zero keeps a new profile invalid until explicitly configured.
        [SerializeField, Range(0f, 1f)]
        private float actionStep = 0f;

        [SerializeField, Min(0f)]
        private float transitionDurationSeconds = 0f;

        [SerializeField, Min(0f)]
        private float minimumSecondsBetweenActions = 0f;

        public string SceneId => sceneId;

        public string DisplayName => displayName;

        public bool ResearchConfigurationApproved => researchConfigurationApproved;

        public bool TryCreateRuntimeProfile(
            out SceneEnvironmentProfile profile,
            out string validationError)
        {
            profile = null;

            if (!researchConfigurationApproved)
            {
                validationError =
                    "Research configuration is not approved for runtime use.";
                return false;
            }

            try
            {
                var safeDefault = new EnvironmentState(
                    defaultIllumination,
                    defaultWarmth,
                    defaultAtmosphericSoftness,
                    defaultColorRichness,
                    defaultAmbientMotion);
                var limits = new EnvironmentStateLimits(
                    CreateRange(illuminationRange),
                    CreateRange(warmthRange),
                    CreateRange(atmosphericSoftnessRange),
                    CreateRange(colorRichnessRange),
                    CreateRange(ambientMotionRange));

                profile = new SceneEnvironmentProfile(
                    sceneId,
                    displayName,
                    safeDefault,
                    limits,
                    actionStep,
                    transitionDurationSeconds,
                    minimumSecondsBetweenActions);
                validationError = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                validationError = exception.Message;
                return false;
            }
        }

        private static NormalizedRange CreateRange(Vector2 serializedRange)
        {
            return new NormalizedRange(serializedRange.x, serializedRange.y);
        }
    }
}
