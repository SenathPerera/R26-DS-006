using System;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Environment
{
    [CreateAssetMenu(
        fileName = "TemplePondEnvironmentMappingProfile",
        menuName = "Adaptive Meditation/Environment/Temple Pond Mapping Profile")]
    public sealed class TemplePondEnvironmentMappingProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string configurationId = string.Empty;

        [SerializeField, Min(0)]
        private int configurationVersion = 0;

        [Tooltip(
            "Enable only after all raw Temple scene mappings have been "
            + "reviewed for the active engineering pilot or study.")]
        [SerializeField]
        private bool researchConfigurationApproved = false;

        [Header("Illumination")]
        // TODO(RESEARCH_DECISION): Calibrate the permitted light-intensity range.
        [SerializeField]
        private Vector2 directionalLightIntensityRange = Vector2.zero;

        [Header("Color Warmth")]
        // TODO(RESEARCH_DECISION): Calibrate plausible cool and warm endpoints.
        [SerializeField]
        private Color coolDirectionalLightColor = Color.white;

        [SerializeField]
        private Color warmDirectionalLightColor = Color.white;

        [Header("Atmospheric Softness")]
        // TODO(RESEARCH_DECISION): Calibrate density and tint on Quest 2.
        [SerializeField]
        private Vector2 fogDensityRange = Vector2.zero;

        [SerializeField]
        private Color clearFogColor = Color.white;

        [SerializeField]
        private Color softFogColor = Color.white;

        [Header("Color Richness")]
        [SerializeField]
        private string waterColorProperty = "_BaseColor";

        // TODO(RESEARCH_DECISION): Calibrate muted and rich water colors.
        [SerializeField]
        private Color mutedWaterColor = Color.white;

        [SerializeField]
        private Color richWaterColor = Color.white;

        [Header("Ambient Motion")]
        [Tooltip(
            "Shader property that controls restrained pond ripple motion. "
            + "The current Temple water shader must expose this explicitly.")]
        [SerializeField]
        private string waterMotionProperty = "_RippleMotion";

        // TODO(RESEARCH_DECISION): Calibrate the permitted ripple-motion range.
        [SerializeField]
        private Vector2 waterMotionRange = Vector2.zero;

        public bool TryCreateRuntimeMapping(
            out TemplePondEnvironmentMapping mapping,
            out string validationError)
        {
            mapping = null;
            if (!researchConfigurationApproved)
            {
                validationError =
                    "Temple scene mapping configuration is not approved for runtime use.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(configurationId))
            {
                validationError = "A Temple mapping configuration ID is required.";
                return false;
            }

            if (configurationVersion < 1)
            {
                validationError =
                    "Temple mapping configuration version must be at least 1.";
                return false;
            }

            if (!TryValidateIncreasingNonNegativeRange(
                    directionalLightIntensityRange,
                    "directional light intensity",
                    out validationError)
                || !TryValidateIncreasingNonNegativeRange(
                    fogDensityRange,
                    "fog density",
                    out validationError)
                || !TryValidateIncreasingNonNegativeRange(
                    waterMotionRange,
                    "water motion",
                    out validationError))
            {
                return false;
            }

            if (!IsNormalizedColor(coolDirectionalLightColor)
                || !IsNormalizedColor(warmDirectionalLightColor)
                || !IsNormalizedColor(clearFogColor)
                || !IsNormalizedColor(softFogColor)
                || !IsNormalizedColor(mutedWaterColor)
                || !IsNormalizedColor(richWaterColor))
            {
                validationError =
                    "Temple mapping colors must contain finite components in [0, 1].";
                return false;
            }

            if (string.IsNullOrWhiteSpace(waterColorProperty)
                || string.IsNullOrWhiteSpace(waterMotionProperty))
            {
                validationError =
                    "Water color and motion shader property names are required.";
                return false;
            }

            mapping = new TemplePondEnvironmentMapping(
                configurationId.Trim(),
                configurationVersion,
                directionalLightIntensityRange,
                coolDirectionalLightColor,
                warmDirectionalLightColor,
                fogDensityRange,
                clearFogColor,
                softFogColor,
                waterColorProperty.Trim(),
                mutedWaterColor,
                richWaterColor,
                waterMotionProperty.Trim(),
                waterMotionRange);
            validationError = string.Empty;
            return true;
        }

        private static bool TryValidateIncreasingNonNegativeRange(
            Vector2 range,
            string label,
            out string validationError)
        {
            if (!IsFinite(range.x)
                || !IsFinite(range.y)
                || range.x < 0f
                || range.y <= range.x)
            {
                validationError = label
                    + " range must be finite, non-negative, and strictly increasing.";
                return false;
            }

            validationError = string.Empty;
            return true;
        }

        private static bool IsNormalizedColor(Color color)
        {
            return IsNormalized(color.r)
                && IsNormalized(color.g)
                && IsNormalized(color.b)
                && IsNormalized(color.a);
        }

        private static bool IsNormalized(float value)
        {
            return IsFinite(value) && value >= 0f && value <= 1f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class TemplePondEnvironmentMapping
    {
        internal TemplePondEnvironmentMapping(
            string configurationId,
            int configurationVersion,
            Vector2 directionalLightIntensityRange,
            Color coolDirectionalLightColor,
            Color warmDirectionalLightColor,
            Vector2 fogDensityRange,
            Color clearFogColor,
            Color softFogColor,
            string waterColorProperty,
            Color mutedWaterColor,
            Color richWaterColor,
            string waterMotionProperty,
            Vector2 waterMotionRange)
        {
            ConfigurationId = configurationId;
            ConfigurationVersion = configurationVersion;
            DirectionalLightIntensityRange = directionalLightIntensityRange;
            CoolDirectionalLightColor = coolDirectionalLightColor;
            WarmDirectionalLightColor = warmDirectionalLightColor;
            FogDensityRange = fogDensityRange;
            ClearFogColor = clearFogColor;
            SoftFogColor = softFogColor;
            WaterColorProperty = waterColorProperty;
            MutedWaterColor = mutedWaterColor;
            RichWaterColor = richWaterColor;
            WaterMotionProperty = waterMotionProperty;
            WaterMotionRange = waterMotionRange;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public Vector2 DirectionalLightIntensityRange { get; }

        public Color CoolDirectionalLightColor { get; }

        public Color WarmDirectionalLightColor { get; }

        public Vector2 FogDensityRange { get; }

        public Color ClearFogColor { get; }

        public Color SoftFogColor { get; }

        public string WaterColorProperty { get; }

        public Color MutedWaterColor { get; }

        public Color RichWaterColor { get; }

        public string WaterMotionProperty { get; }

        public Vector2 WaterMotionRange { get; }
    }
}
