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
        // TODO(RESEARCH_DECISION): Verify all three warmth anchors on Quest 2.
        [SerializeField]
        private Color coolDirectionalLightColor = Color.white;

        [Tooltip(
            "Directional-light color at normalized warmth 0.5. This preserves "
            + "the scene's calibrated neutral appearance.")]
        [SerializeField]
        private Color neutralDirectionalLightColor = Color.white;

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
        [Tooltip(
            "URP Color Adjustments saturation values mapped from normalized "
            + "color richness. Supported values are [-100, 100].")]
        // TODO(RESEARCH_DECISION): Calibrate a restrained scene-wide saturation
        // range on Quest 2 before approving this mapping for research use.
        [SerializeField]
        private Vector2 saturationRange = Vector2.zero;

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
                || !TryValidateSaturationRange(
                    saturationRange,
                    out validationError)
                || !TryValidateIncreasingNonNegativeRange(
                    waterMotionRange,
                    "water motion",
                    out validationError))
            {
                return false;
            }

            if (!IsNormalizedColor(coolDirectionalLightColor)
                || !IsNormalizedColor(neutralDirectionalLightColor)
                || !IsNormalizedColor(warmDirectionalLightColor)
                || !IsNormalizedColor(clearFogColor)
                || !IsNormalizedColor(softFogColor))
            {
                validationError =
                    "Temple mapping colors must contain finite components in [0, 1].";
                return false;
            }

            if (string.IsNullOrWhiteSpace(waterMotionProperty))
            {
                validationError =
                    "A water motion shader property name is required.";
                return false;
            }

            mapping = new TemplePondEnvironmentMapping(
                configurationId.Trim(),
                configurationVersion,
                directionalLightIntensityRange,
                coolDirectionalLightColor,
                neutralDirectionalLightColor,
                warmDirectionalLightColor,
                fogDensityRange,
                clearFogColor,
                softFogColor,
                saturationRange,
                waterMotionProperty.Trim(),
                waterMotionRange);
            validationError = string.Empty;
            return true;
        }

        private static bool TryValidateSaturationRange(
            Vector2 range,
            out string validationError)
        {
            if (!IsFinite(range.x)
                || !IsFinite(range.y)
                || range.x < -100f
                || range.y > 100f
                || range.y <= range.x)
            {
                validationError =
                    "saturation range must be finite, strictly increasing, "
                    + "and contained within [-100, 100].";
                return false;
            }

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
            Color neutralDirectionalLightColor,
            Color warmDirectionalLightColor,
            Vector2 fogDensityRange,
            Color clearFogColor,
            Color softFogColor,
            Vector2 saturationRange,
            string waterMotionProperty,
            Vector2 waterMotionRange)
        {
            ConfigurationId = configurationId;
            ConfigurationVersion = configurationVersion;
            DirectionalLightIntensityRange = directionalLightIntensityRange;
            CoolDirectionalLightColor = coolDirectionalLightColor;
            NeutralDirectionalLightColor = neutralDirectionalLightColor;
            WarmDirectionalLightColor = warmDirectionalLightColor;
            FogDensityRange = fogDensityRange;
            ClearFogColor = clearFogColor;
            SoftFogColor = softFogColor;
            SaturationRange = saturationRange;
            WaterMotionProperty = waterMotionProperty;
            WaterMotionRange = waterMotionRange;
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public Vector2 DirectionalLightIntensityRange { get; }

        public Color CoolDirectionalLightColor { get; }

        public Color NeutralDirectionalLightColor { get; }

        public Color WarmDirectionalLightColor { get; }

        public Vector2 FogDensityRange { get; }

        public Color ClearFogColor { get; }

        public Color SoftFogColor { get; }

        public Vector2 SaturationRange { get; }

        public string WaterMotionProperty { get; }

        public Vector2 WaterMotionRange { get; }

        public Color MapDirectionalLightColor(float normalizedWarmth)
        {
            if (float.IsNaN(normalizedWarmth)
                || float.IsInfinity(normalizedWarmth)
                || normalizedWarmth < 0f
                || normalizedWarmth > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(normalizedWarmth),
                    normalizedWarmth,
                    "Normalized warmth must be finite and in [0, 1].");
            }

            if (normalizedWarmth <= 0.5f)
            {
                return Color.Lerp(
                    CoolDirectionalLightColor,
                    NeutralDirectionalLightColor,
                    normalizedWarmth * 2f);
            }

            return Color.Lerp(
                NeutralDirectionalLightColor,
                WarmDirectionalLightColor,
                (normalizedWarmth - 0.5f) * 2f);
        }
    }
}
