using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using UnityEditor;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public static class FrozenEvaluationProfileLoader
    {
        private const string ConfigurationRoot =
            "Assets/Laminar VR/Configuration/";

        public static bool TryLoad(
            out EvaluationRuntimeBundle bundle,
            out string validationError)
        {
            bundle = null;

            if (!TryLoadAsset(
                    ConfigurationRoot
                        + "Environment/TemplePondSceneParameterProfile.asset",
                    out SceneParameterProfile scene,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Session/TemplePondSessionTimingProfile.asset",
                    out SessionTimingProfile timing,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Physiology/ComponentBPhysiologyValidationProfile.asset",
                    out PhysiologyValidationProfile physiology,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Rewards/AdaptiveVrRewardPipelineProfile.asset",
                    out RewardPipelineProfile reward,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Application/AdaptiveVrProductionCoordinatorProfile.asset",
                    out ProductionCoordinatorProfile coordinator,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Preferences/AdaptiveVrPreferenceInitializationProfile.asset",
                    out PreferenceInitializationProfile preference,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Policies/AdaptiveVrRuleBasedPolicyProfile.asset",
                    out RuleBasedPolicyProfile ruleBased,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Policies/AdaptiveVrLinUcbPolicyProfile.asset",
                    out LinUcbPolicyProfile linUcb,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Stabilization/AdaptiveVrStabilizationSelectionProfile.asset",
                    out StabilizationSelectionProfile stabilization,
                    out validationError)
                || !TryLoadAsset(
                    ConfigurationRoot
                        + "Telemetry/AdaptiveVrTelemetryLoggingProfile.asset",
                    out TelemetryLoggingProfile telemetry,
                    out validationError))
            {
                return false;
            }

            var featureBuilder = new PolicyFeatureVectorBuilder();
            if (!scene.TryCreateRuntimeProfile(
                    out var sceneConfiguration,
                    out validationError)
                || !timing.TryCreateRuntimeConfiguration(
                    out var timingConfiguration,
                    out validationError)
                || !physiology.TryCreateRuntimeConfiguration(
                    out var physiologyConfiguration,
                    out validationError)
                || !reward.TryCreateRuntimeConfiguration(
                    out var rewardConfiguration,
                    out validationError)
                || !coordinator.TryCreateRuntimeConfiguration(
                    out var coordinatorConfiguration,
                    out validationError)
                || !preference.TryCreateRuntimeConfiguration(
                    out var preferenceConfiguration,
                    out validationError)
                || !ruleBased.TryCreateRuntimeConfiguration(
                    out var ruleConfiguration,
                    out validationError)
                || !linUcb.TryCreateRuntimeConfiguration(
                    featureBuilder,
                    out var linUcbConfiguration,
                    out validationError)
                || !stabilization.TryCreateRuntimeConfiguration(
                    out var stabilizationConfiguration,
                    out validationError)
                || !telemetry.TryCreateRuntimeConfiguration(
                    out var telemetryConfiguration,
                    out validationError))
            {
                return false;
            }

            try
            {
                bundle = new EvaluationRuntimeBundle(
                    sceneConfiguration,
                    timingConfiguration,
                    physiologyConfiguration,
                    rewardConfiguration,
                    coordinatorConfiguration,
                    preferenceConfiguration,
                    ruleConfiguration,
                    linUcbConfiguration,
                    stabilizationConfiguration,
                    telemetryConfiguration,
                    featureBuilder);
                validationError = string.Empty;
                return true;
            }
            catch (System.Exception exception) when (
                exception is System.ArgumentException
                || exception is System.InvalidOperationException)
            {
                validationError = exception.Message;
                return false;
            }
        }

        private static bool TryLoadAsset<T>(
            string path,
            out T asset,
            out string validationError)
            where T : UnityEngine.Object
        {
            asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                validationError = string.Empty;
                return true;
            }

            validationError = "Required frozen profile is missing: " + path;
            return false;
        }
    }
}
