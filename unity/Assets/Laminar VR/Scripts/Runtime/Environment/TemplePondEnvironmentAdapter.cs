using System;
using LaminarVR.AdaptiveMeditation.Environment;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LaminarVR.AdaptiveMeditation.Runtime.Environment
{
    [AddComponentMenu(
        "Adaptive Meditation/Environment/Temple Pond Environment Adapter")]
    [DisallowMultipleComponent]
    public sealed class TemplePondEnvironmentAdapter
        : MonoBehaviour, ISceneEnvironmentAdapter
    {
        private const string DefaultSceneId = "temple-pond";

        [Header("Identity and Configuration")]
        [SerializeField]
        private string sceneId = DefaultSceneId;

        [SerializeField]
        private TemplePondEnvironmentMappingProfile mappingProfile = null;

        [Header("Required Scene Bindings")]
        [SerializeField]
        private Light mainDirectionalLight = null;

        [SerializeField]
        private Renderer pondWaterRenderer = null;

        [SerializeField]
        private Volume globalColorVolume = null;

        private TemplePondEnvironmentMapping mapping;
        private ColorAdjustments colorAdjustments;
        private MaterialPropertyBlock waterPropertyBlock;
        private int waterMotionPropertyId;

        public string SceneId => sceneId == null ? string.Empty : sceneId.Trim();

        public bool IsInitialized => mapping != null;

        public bool HasAppliedState { get; private set; }

        public EnvironmentState LastAppliedState { get; private set; }

        public void Configure(
            TemplePondEnvironmentMappingProfile profile,
            Light directionalLight,
            Renderer waterRenderer,
            Volume colorVolume)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "A Temple adapter cannot be reconfigured after initialization.");
            }

            mappingProfile = profile;
            mainDirectionalLight = directionalLight;
            pondWaterRenderer = waterRenderer;
            globalColorVolume = colorVolume;
        }

        public SceneBindingValidation ValidateBindings()
        {
            if (string.IsNullOrWhiteSpace(SceneId))
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.SceneIdMissing,
                    "Assign a stable Temple scene ID.");
            }

            if (mappingProfile == null)
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.ConfigurationMissing,
                    "Assign a TemplePondEnvironmentMappingProfile.");
            }

            if (!mappingProfile.TryCreateRuntimeMapping(
                    out var candidateMapping,
                    out var mappingError))
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.ConfigurationInvalid,
                    mappingError);
            }

            if (mainDirectionalLight == null)
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.RequiredReferenceMissing,
                    "Assign the scene's primary directional Light.");
            }

            if (mainDirectionalLight.type != LightType.Directional)
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.RequiredReferenceMissing,
                    "The primary light binding must be a directional Light.");
            }

            if (pondWaterRenderer == null
                || pondWaterRenderer.sharedMaterial == null)
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.RequiredReferenceMissing,
                    "Assign a pond-water Renderer with a shared material.");
            }

            if (globalColorVolume == null)
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.RequiredReferenceMissing,
                    "Assign the scene's global color-adjustment Volume.");
            }

            if (!globalColorVolume.isGlobal)
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.RequiredReferenceMissing,
                    "The color-adjustment Volume must be global.");
            }

            var sharedVolumeProfile = globalColorVolume.sharedProfile;
            if (sharedVolumeProfile == null
                || !sharedVolumeProfile.TryGet<ColorAdjustments>(
                    out var sharedColorAdjustments))
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.RequiredReferenceMissing,
                    "The global Volume profile must contain Color Adjustments.");
            }

            if (!sharedColorAdjustments.active
                || !sharedColorAdjustments.saturation.overrideState)
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.ConfigurationInvalid,
                    "Color Adjustments must be active with Saturation overridden.");
            }

            var material = pondWaterRenderer.sharedMaterial;
            if (!material.HasProperty(candidateMapping.WaterMotionProperty))
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.ShaderPropertyMissing,
                    "The pond-water material does not expose motion property '"
                    + candidateMapping.WaterMotionProperty
                    + "'.");
            }

            return SceneBindingValidation.Succeeded();
        }

        public bool TryInitialize(out string validationError)
        {
            if (IsInitialized)
            {
                validationError = string.Empty;
                return true;
            }

            var bindingValidation = ValidateBindings();
            if (!bindingValidation.IsValid)
            {
                validationError = bindingValidation.Code
                    + ": "
                    + bindingValidation.Detail;
                return false;
            }

            if (!mappingProfile.TryCreateRuntimeMapping(
                    out mapping,
                    out validationError))
            {
                return false;
            }

            var runtimeVolumeProfile = globalColorVolume.profile;
            if (!runtimeVolumeProfile.TryGet(out colorAdjustments))
            {
                validationError =
                    "The runtime Volume profile does not contain Color Adjustments.";
                mapping = null;
                return false;
            }

            waterMotionPropertyId = Shader.PropertyToID(
                mapping.WaterMotionProperty);
            waterPropertyBlock = new MaterialPropertyBlock();
            validationError = string.Empty;
            return true;
        }

        public void ApplyState(EnvironmentState state)
        {
            if (!state.IsNormalized)
            {
                throw new ArgumentException(
                    "A scene adapter requires a normalized environment state.",
                    nameof(state));
            }

            if (!TryInitialize(out var validationError))
            {
                throw new InvalidOperationException(
                    "Temple scene bindings are invalid. " + validationError);
            }

            mainDirectionalLight.intensity = Mathf.Lerp(
                mapping.DirectionalLightIntensityRange.x,
                mapping.DirectionalLightIntensityRange.y,
                state.Illumination);
            mainDirectionalLight.color =
                mapping.MapDirectionalLightColor(state.Warmth);

            RenderSettings.fog = true;
            RenderSettings.fogDensity = Mathf.Lerp(
                mapping.FogDensityRange.x,
                mapping.FogDensityRange.y,
                state.AtmosphericSoftness);
            RenderSettings.fogColor = Color.Lerp(
                mapping.ClearFogColor,
                mapping.SoftFogColor,
                state.AtmosphericSoftness);

            colorAdjustments.saturation.value = Mathf.Lerp(
                mapping.SaturationRange.x,
                mapping.SaturationRange.y,
                state.ColorRichness);

            pondWaterRenderer.GetPropertyBlock(waterPropertyBlock);
            waterPropertyBlock.SetFloat(
                waterMotionPropertyId,
                Mathf.Lerp(
                    mapping.WaterMotionRange.x,
                    mapping.WaterMotionRange.y,
                    state.AmbientMotion));
            pondWaterRenderer.SetPropertyBlock(waterPropertyBlock);

            LastAppliedState = state;
            HasAppliedState = true;
        }
    }
}
