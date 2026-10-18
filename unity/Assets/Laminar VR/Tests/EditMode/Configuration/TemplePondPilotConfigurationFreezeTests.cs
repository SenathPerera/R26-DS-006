using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Runtime.Environment;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class TemplePondPilotConfigurationFreezeTests
    {
        private const double Tolerance = 1e-6d;

        [Test]
        public void FrozenPilotProfiles_MatchApprovedBundle()
        {
            AssertSceneProfile();
            AssertMappingProfile();
            AssertPreferenceProfile();
            AssertRuleProfile();
            AssertLinUcbProfile();
            AssertSessionProfile();
            AssertPhysiologyProfile();
            AssertRewardProfile();
            AssertStabilizationProfile();
            AssertTelemetryProfile();
            AssertCoordinatorProfile();
            AssertReconnectProfile();
            Assert.That(
                new PolicyFeatureVectorBuilder().FeatureSchemaVersion,
                Is.EqualTo("adaptive-vr-policy-features/1.0"));
            AssertRelaySchema();
            Assert.That(
                SessionContextSnapshot.SupportedSchemaVersion,
                Is.EqualTo("mindsync-session-context-v1"));
        }

        [Test]
        public void PreviousVisualProfiles_RetainPilotV2Values()
        {
            var sceneAsset = Load<SceneParameterProfile>(
                "Environment/TemplePondSceneParameterProfile.asset");
            Assert.That(sceneAsset.TryCreateRuntimeProfile(
                out var sceneProfile, out var sceneError), Is.True, sceneError);
            Assert.That(sceneProfile.ActionSteps.Illumination,
                Is.EqualTo(0.1d).Within(Tolerance));
            Assert.That(sceneProfile.ActionSteps.Warmth,
                Is.EqualTo(0.25d).Within(Tolerance));
            Assert.That(sceneProfile.ActionSteps.AtmosphericSoftness,
                Is.EqualTo(0.3d).Within(Tolerance));
            Assert.That(sceneProfile.ActionSteps.ColorRichness,
                Is.EqualTo(0.2d).Within(Tolerance));
            Assert.That(sceneProfile.ActionSteps.AmbientMotion,
                Is.EqualTo(0.2d).Within(Tolerance));
            Assert.That(sceneProfile.TransitionDurationSeconds,
                Is.EqualTo(4.12d).Within(Tolerance));

            var mappingAsset = Load<TemplePondEnvironmentMappingProfile>(
                "Environment/TemplePondEnvironmentMappingProfile.asset");
            Assert.That(mappingAsset.TryCreateRuntimeMapping(
                out var mapping, out var mappingError), Is.True, mappingError);
            Assert.That(mapping.ConfigurationId,
                Is.EqualTo("temple-pond-mapping-pilot-v3"));
            Assert.That(mapping.ConfigurationVersion, Is.EqualTo(3));
            AssertVector(mapping.SaturationRange, -20d, 20d);
            AssertVector(mapping.WaterMotionRange, 0.2d, 0.45d);
        }

        private static void AssertSceneProfile()
        {
            var asset = Load<SceneParameterProfile>(
                "Environment/TemplePondSceneParameterProfile_VisualV3.asset");
            Assert.That(
                asset.TryCreateRuntimeProfile(
                    out var profile,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(profile.SceneId, Is.EqualTo("temple-pond"));
            AssertState(
                profile.SafeDefault,
                0.319d,
                0.5d,
                0d,
                0.5d,
                0.75d);
            AssertRange(profile.Limits.Illumination, 0d, 1d);
            AssertRange(profile.Limits.Warmth, 0d, 1d);
            AssertRange(profile.Limits.AtmosphericSoftness, 0d, 1d);
            AssertRange(profile.Limits.ColorRichness, 0d, 1d);
            AssertRange(profile.Limits.AmbientMotion, 0d, 1d);
            Assert.That(
                profile.ActionSteps.Illumination,
                Is.EqualTo(0.15d).Within(Tolerance));
            Assert.That(
                profile.ActionSteps.Warmth,
                Is.EqualTo(0.35d).Within(Tolerance));
            Assert.That(
                profile.ActionSteps.AtmosphericSoftness,
                Is.EqualTo(0.4d).Within(Tolerance));
            Assert.That(
                profile.ActionSteps.ColorRichness,
                Is.EqualTo(0.3d).Within(Tolerance));
            Assert.That(
                profile.ActionSteps.AmbientMotion,
                Is.EqualTo(0.3d).Within(Tolerance));
            Assert.That(
                profile.TransitionDurationSeconds,
                Is.EqualTo(6d).Within(Tolerance));
            Assert.That(
                profile.MinimumSecondsBetweenActions,
                Is.Zero.Within(Tolerance));
        }

        private static void AssertMappingProfile()
        {
            var asset = Load<TemplePondEnvironmentMappingProfile>(
                "Environment/TemplePondEnvironmentMappingProfile_v4.asset");
            Assert.That(
                asset.TryCreateRuntimeMapping(
                    out var mapping,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                mapping.ConfigurationId,
                Is.EqualTo("temple-pond-mapping-pilot-v4"));
            Assert.That(mapping.ConfigurationVersion, Is.EqualTo(4));
            AssertVector(mapping.DirectionalLightIntensityRange, 0.6d, 9d);
            AssertColor(
                mapping.CoolDirectionalLightColor,
                0.8666667d,
                0.9254902d,
                1d,
                1d);
            AssertColor(
                mapping.NeutralDirectionalLightColor,
                1d,
                0.91485906d,
                0.8584906d,
                1d);
            AssertColor(
                mapping.WarmDirectionalLightColor,
                1d,
                0.7137255d,
                0.48235294d,
                1d);
            AssertVector(mapping.FogDensityRange, 0d, 0.015d);
            AssertColor(
                mapping.ClearFogColor,
                0.63529414d,
                0.74509805d,
                0.77254903d,
                1d);
            AssertColor(
                mapping.SoftFogColor,
                0.63529414d,
                0.74509805d,
                0.77254903d,
                1d);
            AssertVector(mapping.SaturationRange, -30d, 30d);
            Assert.That(mapping.WaterMotionProperty, Is.EqualTo("_RippleMotion"));
            AssertVector(mapping.WaterMotionRange, 0.1d, 0.6d);
        }

        private static void AssertPreferenceProfile()
        {
            var asset = Load<PreferenceInitializationProfile>(
                "Preferences/AdaptiveVrPreferenceInitializationProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("adaptive-vr-preference-pilot-v1"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(
                configuration.PreferenceWeight,
                Is.EqualTo(0.65d).Within(Tolerance));
        }

        private static void AssertRuleProfile()
        {
            var asset = Load<RuleBasedPolicyProfile>(
                "Policies/AdaptiveVrRuleBasedPolicyProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("adaptive-vr-rule-based-pilot-v1"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(
                configuration.ActivationMode,
                Is.EqualTo(RuleActivationMode.WorseningTrendOrElevatedStress));
            Assert.That(
                configuration.MinimumContinuousStressScore,
                Is.EqualTo(2d).Within(Tolerance));
            Assert.That(
                configuration.MinimumStressIncreasePerMinute,
                Is.EqualTo(0.5d).Within(Tolerance));
            Assert.That(
                configuration.MinimumPreferenceDelta,
                Is.EqualTo(0.05d).Within(Tolerance));
        }

        private static void AssertLinUcbProfile()
        {
            var asset = Load<LinUcbPolicyProfile>(
                "Policies/AdaptiveVrLinUcbPolicyProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    new PolicyFeatureVectorBuilder(),
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("adaptive-vr-linucb-pilot-v1"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(
                configuration.RidgeRegularization,
                Is.EqualTo(1d).Within(Tolerance));
            Assert.That(
                configuration.ExplorationCoefficient,
                Is.EqualTo(0.25d).Within(Tolerance));
        }

        private static void AssertSessionProfile()
        {
            var asset = Load<SessionTimingProfile>(
                "Session/TemplePondSessionTimingProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("temple-pond-session-pilot-v1"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(configuration.AcclimatizationDurationSeconds, Is.EqualTo(120d));
            Assert.That(configuration.AdaptiveDurationSeconds, Is.EqualTo(900d));
            Assert.That(configuration.StabilizationDurationSeconds, Is.EqualTo(150d));
            Assert.That(configuration.DecisionIntervalSeconds, Is.EqualTo(75d));
        }

        private static void AssertPhysiologyProfile()
        {
            var asset = Load<PhysiologyValidationProfile>(
                "Physiology/ComponentBPhysiologyValidationProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("component-b-physiology-pilot-v1"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(configuration.StaleAfterSeconds, Is.EqualTo(90d).Within(Tolerance));
            Assert.That(configuration.MinimumWindowDurationSeconds, Is.EqualTo(30d).Within(Tolerance));
            Assert.That(configuration.MaximumFutureClockSkewSeconds, Is.EqualTo(2d).Within(Tolerance));
            Assert.That(configuration.SourceTimestampToleranceSeconds, Is.EqualTo(0.001d).Within(Tolerance));
            Assert.That(configuration.ProbabilitySumTolerance, Is.EqualTo(0.005d).Within(Tolerance));
            Assert.That(configuration.MinimumDecisionSignalQuality, Is.EqualTo(0.5d).Within(Tolerance));
            Assert.That(configuration.MinimumRewardSignalQuality, Is.EqualTo(0.5d).Within(Tolerance));
            Assert.That(configuration.MaximumBufferedWindows, Is.EqualTo(8));
        }

        private static void AssertRewardProfile()
        {
            var asset = Load<RewardPipelineProfile>(
                "Rewards/AdaptiveVrRewardPipelineProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("adaptive-vr-reward-pilot-v2"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
            Assert.That(
                configuration.BaselineStandardDeviationMethod,
                Is.EqualTo(BaselineStandardDeviationMethod.Population));
            Assert.That(configuration.MinimumBaselineSamples, Is.EqualTo(2));
            Assert.That(
                configuration.MinimumBaselineStandardDeviation,
                Is.EqualTo(0.01d).Within(Tolerance));
            Assert.That(configuration.TrendWindowCount, Is.EqualTo(5));
            Assert.That(configuration.MinimumTrendSamples, Is.EqualTo(3));
            Assert.That(configuration.SettlingSeconds, Is.EqualTo(5d).Within(Tolerance));
            Assert.That(configuration.MaximumAttributionWaitSeconds, Is.EqualTo(120d).Within(Tolerance));
            Assert.That(configuration.StressWeight, Is.EqualTo(1d).Within(Tolerance));
            Assert.That(configuration.RmssdWeight, Is.EqualTo(0.35d).Within(Tolerance));
            Assert.That(configuration.HeartRateWeight, Is.EqualTo(0.15d).Within(Tolerance));
            Assert.That(configuration.ChangePenaltyWeight, Is.EqualTo(0.1d).Within(Tolerance));
            Assert.That(configuration.DiscomfortPenaltyWeight, Is.Zero.Within(Tolerance));
            Assert.That(configuration.SafetyPenaltyWeight, Is.EqualTo(2d).Within(Tolerance));
        }

        private static void AssertStabilizationProfile()
        {
            var asset = Load<StabilizationSelectionProfile>(
                "Stabilization/AdaptiveVrStabilizationSelectionProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("adaptive-vr-stabilization-pilot-v1"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(configuration.RecentOutcomeCount, Is.EqualTo(4));
            Assert.That(
                configuration.RewardRecencyDecay,
                Is.EqualTo(0.8d).Within(Tolerance));
            Assert.That(
                configuration.PreferenceDistancePenaltyWeight,
                Is.EqualTo(0.25d).Within(Tolerance));
        }

        private static void AssertTelemetryProfile()
        {
            var asset = Load<TelemetryLoggingProfile>(
                "Telemetry/AdaptiveVrTelemetryLoggingProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("adaptive-vr-telemetry-pilot-v3"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(3));
            Assert.That(configuration.EventSchemaId, Is.EqualTo("adaptive-vr-telemetry"));
            Assert.That(configuration.EventSchemaVersion, Is.EqualTo("1.1-pilot"));
            Assert.That(configuration.FlushEveryEventCount, Is.EqualTo(8));
        }

        private static void AssertCoordinatorProfile()
        {
            var asset = Load<ProductionCoordinatorProfile>(
                "Application/AdaptiveVrProductionCoordinatorProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("adaptive-vr-coordinator-pilot-v1"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(
                configuration.ExpectedPhysiologyOutputIntervalSeconds,
                Is.EqualTo(60d).Within(Tolerance));
            Assert.That(
                configuration.SafetyLimits.MaximumConsecutiveSameDirectionActions,
                Is.EqualTo(2));
            Assert.That(
                configuration.SafetyLimits.MaximumTotalVariation,
                Is.EqualTo(0.9d).Within(Tolerance));
        }

        private static void AssertReconnectProfile()
        {
            var asset = Load<ReconnectBackoffProfile>(
                "Networking/ComponentBQuestReconnectBackoffProfile.asset");
            Assert.That(
                asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError),
                Is.True,
                validationError);
            Assert.That(
                configuration.ConfigurationId,
                Is.EqualTo("component-b-quest-reconnect-pilot-v1"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(configuration.MaximumAttempts, Is.EqualTo(8));
            Assert.That(configuration.InitialDelaySeconds, Is.EqualTo(1d).Within(Tolerance));
            Assert.That(configuration.MaximumDelaySeconds, Is.EqualTo(16d).Within(Tolerance));
            Assert.That(configuration.DelayMultiplier, Is.EqualTo(2d).Within(Tolerance));
        }

        private static void AssertRelaySchema()
        {
            var asset = Load<SessionRelayConnectionProfile>(
                "Networking/TemplePondDevelopmentSessionRelayProfile.asset");
            var serialized = new SerializedObject(asset);
            Assert.That(
                serialized.FindProperty("schemaVersion").stringValue,
                Is.EqualTo("mindsync-session-v3"));
        }

        private static T Load<T>(string relativePath) where T : Object
        {
            var path = "Assets/Laminar VR/Configuration/" + relativePath;
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, "Missing frozen asset at " + path);
            return asset;
        }

        private static void AssertState(
            EnvironmentState state,
            double illumination,
            double warmth,
            double softness,
            double richness,
            double motion)
        {
            Assert.That(state.Illumination, Is.EqualTo(illumination).Within(Tolerance));
            Assert.That(state.Warmth, Is.EqualTo(warmth).Within(Tolerance));
            Assert.That(state.AtmosphericSoftness, Is.EqualTo(softness).Within(Tolerance));
            Assert.That(state.ColorRichness, Is.EqualTo(richness).Within(Tolerance));
            Assert.That(state.AmbientMotion, Is.EqualTo(motion).Within(Tolerance));
        }

        private static void AssertRange(
            NormalizedRange range,
            double minimum,
            double maximum)
        {
            Assert.That(range.Minimum, Is.EqualTo(minimum).Within(Tolerance));
            Assert.That(range.Maximum, Is.EqualTo(maximum).Within(Tolerance));
        }

        private static void AssertVector(
            Vector2 vector,
            double x,
            double y)
        {
            Assert.That(vector.x, Is.EqualTo(x).Within(Tolerance));
            Assert.That(vector.y, Is.EqualTo(y).Within(Tolerance));
        }

        private static void AssertColor(
            Color color,
            double red,
            double green,
            double blue,
            double alpha)
        {
            Assert.That(color.r, Is.EqualTo(red).Within(Tolerance));
            Assert.That(color.g, Is.EqualTo(green).Within(Tolerance));
            Assert.That(color.b, Is.EqualTo(blue).Within(Tolerance));
            Assert.That(color.a, Is.EqualTo(alpha).Within(Tolerance));
        }
    }
}
