using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu("Adaptive Meditation/Application/Application Bootstrap")]
    [DisallowMultipleComponent]
    public sealed class ApplicationBootstrap : MonoBehaviour
    {
        [Header("Startup")]
        [SerializeField]
        private bool initializeOnStart = true;

        [Header("Scene")]
        [SerializeField]
        private SceneParameterProfile sceneParameterProfile = null;

        [Tooltip(
            "A scene component implementing ISceneEnvironmentAdapter. "
            + "Use an explicit reference; runtime scene searches are not used.")]
        [SerializeField]
        private MonoBehaviour sceneAdapterComponent = null;

        [Header("Study Policy")]
        [SerializeField]
        private StudyPolicyMode studyPolicyMode =
            StudyPolicyMode.StaticPersonalized;

        [SerializeField]
        private RuleBasedPolicyProfile ruleBasedPolicyProfile = null;

        [SerializeField]
        private LinUcbPolicyProfile linUcbPolicyProfile = null;

        public bool IsInitialized { get; private set; }

        public string LastValidationError { get; private set; } = string.Empty;

        public SceneEnvironmentProfile SceneProfile { get; private set; }

        public IEnvironmentPolicy Policy { get; private set; }

        public EnvironmentParameterManager EnvironmentManager { get; private set; }

        private void Start()
        {
            if (!initializeOnStart || IsInitialized)
            {
                return;
            }

            if (TryInitialize(out var validationError))
            {
                Debug.Log(
                    "[ApplicationBootstrap] initialized"
                    + " scene_id=" + SceneProfile.SceneId
                    + " policy_id=" + Policy.PolicyId,
                    this);
                return;
            }

            enabled = false;
            Debug.LogError(
                "[ApplicationBootstrap] initialization_failed reason="
                + validationError,
                this);
        }

        public void Configure(
            SceneParameterProfile profile,
            MonoBehaviour adapterComponent,
            StudyPolicyMode policyMode,
            RuleBasedPolicyProfile ruleProfile = null,
            LinUcbPolicyProfile linUcbProfile = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "ApplicationBootstrap cannot be reconfigured after initialization.");
            }

            sceneParameterProfile = profile;
            sceneAdapterComponent = adapterComponent;
            studyPolicyMode = policyMode;
            ruleBasedPolicyProfile = ruleProfile;
            linUcbPolicyProfile = linUcbProfile;
        }

        public bool TryInitialize(out string validationError)
        {
            if (IsInitialized)
            {
                validationError = string.Empty;
                return true;
            }

            if (sceneParameterProfile == null)
            {
                return Fail("Assign a SceneParameterProfile.", out validationError);
            }

            if (!(sceneAdapterComponent is ISceneEnvironmentAdapter adapter))
            {
                return Fail(
                    "Assign a MonoBehaviour implementing ISceneEnvironmentAdapter.",
                    out validationError);
            }

            var bindingValidation = adapter.ValidateBindings();
            if (bindingValidation == null || !bindingValidation.IsValid)
            {
                var detail = bindingValidation == null
                    ? "No binding validation result was returned."
                    : bindingValidation.Code + ": " + bindingValidation.Detail;
                return Fail(
                    "Scene adapter bindings are invalid. " + detail,
                    out validationError);
            }

            if (!sceneParameterProfile.TryCreateRuntimeProfile(
                    out var runtimeSceneProfile,
                    out var sceneProfileError))
            {
                return Fail(sceneProfileError, out validationError);
            }

            if (!string.Equals(
                    runtimeSceneProfile.SceneId,
                    adapter.SceneId,
                    StringComparison.Ordinal))
            {
                return Fail(
                    "Scene profile ID '"
                    + runtimeSceneProfile.SceneId
                    + "' does not match adapter ID '"
                    + adapter.SceneId
                    + "'.",
                    out validationError);
            }

            var featureVectorBuilder = new PolicyFeatureVectorBuilder();
            RuleBasedPolicyConfiguration ruleConfiguration = null;
            LinUcbModelConfiguration linUcbConfiguration = null;
            if (studyPolicyMode == StudyPolicyMode.RuleBasedAdaptive)
            {
                if (ruleBasedPolicyProfile == null)
                {
                    return Fail(
                        "Assign an approved RuleBasedPolicyProfile.",
                        out validationError);
                }

                if (!ruleBasedPolicyProfile.TryCreateRuntimeConfiguration(
                        out ruleConfiguration,
                        out var ruleError))
                {
                    return Fail(ruleError, out validationError);
                }
            }
            else if (studyPolicyMode == StudyPolicyMode.ContextualBandit)
            {
                if (linUcbPolicyProfile == null)
                {
                    return Fail(
                        "Assign an approved LinUcbPolicyProfile.",
                        out validationError);
                }

                if (!linUcbPolicyProfile.TryCreateRuntimeConfiguration(
                        featureVectorBuilder,
                        out linUcbConfiguration,
                        out var linUcbError))
                {
                    return Fail(linUcbError, out validationError);
                }
            }

            if (!StudyPolicyFactory.TryCreate(
                    studyPolicyMode,
                    ruleConfiguration,
                    linUcbConfiguration,
                    featureVectorBuilder,
                    out var policy,
                    out var creationResult))
            {
                return Fail(
                    "Policy creation failed: " + creationResult + ".",
                    out validationError);
            }

            try
            {
                EnvironmentManager = new EnvironmentParameterManager(
                    runtimeSceneProfile.SafeDefault,
                    adapter);
            }
            catch (ArgumentException exception)
            {
                return Fail(exception.Message, out validationError);
            }
            catch (InvalidOperationException exception)
            {
                return Fail(exception.Message, out validationError);
            }

            SceneProfile = runtimeSceneProfile;
            Policy = policy;
            IsInitialized = true;
            LastValidationError = string.Empty;
            validationError = string.Empty;
            return true;
        }

        private bool Fail(string reason, out string validationError)
        {
            LastValidationError = string.IsNullOrWhiteSpace(reason)
                ? "Unknown bootstrap validation error."
                : reason.Trim();
            validationError = LastValidationError;
            return false;
        }
    }
}
