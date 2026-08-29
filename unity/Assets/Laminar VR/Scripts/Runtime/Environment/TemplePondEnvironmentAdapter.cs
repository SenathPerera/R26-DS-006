using System;
using LaminarVR.AdaptiveMeditation.Environment;
using UnityEngine;

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

        private TemplePondEnvironmentMapping mapping;
        private MaterialPropertyBlock waterPropertyBlock;
        private int waterColorPropertyId;
        private int waterMotionPropertyId;

        public string SceneId => sceneId == null ? string.Empty : sceneId.Trim();

        public bool IsInitialized => mapping != null;

        public bool HasAppliedState { get; private set; }

        public EnvironmentState LastAppliedState { get; private set; }

        public void Configure(
            TemplePondEnvironmentMappingProfile profile,
            Light directionalLight,
            Renderer waterRenderer)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "A Temple adapter cannot be reconfigured after initialization.");
            }

            mappingProfile = profile;
            mainDirectionalLight = directionalLight;
            pondWaterRenderer = waterRenderer;
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

            var material = pondWaterRenderer.sharedMaterial;
            if (!material.HasProperty(candidateMapping.WaterColorProperty))
            {
                return SceneBindingValidation.Failed(
                    SceneBindingValidationCode.ShaderPropertyMissing,
                    "The pond-water material does not expose color property '"
                    + candidateMapping.WaterColorProperty
                    + "'.");
            }

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

            waterColorPropertyId = Shader.PropertyToID(
                mapping.WaterColorProperty);
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
            mainDirectionalLight.color = Color.Lerp(
                mapping.CoolDirectionalLightColor,
                mapping.WarmDirectionalLightColor,
                state.Warmth);

            RenderSettings.fog = true;
            RenderSettings.fogDensity = Mathf.Lerp(
                mapping.FogDensityRange.x,
                mapping.FogDensityRange.y,
                state.AtmosphericSoftness);
            RenderSettings.fogColor = Color.Lerp(
                mapping.ClearFogColor,
                mapping.SoftFogColor,
                state.AtmosphericSoftness);

            pondWaterRenderer.GetPropertyBlock(waterPropertyBlock);
            waterPropertyBlock.SetColor(
                waterColorPropertyId,
                Color.Lerp(
                    mapping.MutedWaterColor,
                    mapping.RichWaterColor,
                    state.ColorRichness));
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
