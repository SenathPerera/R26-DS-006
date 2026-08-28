using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class RewardPipelineProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<RewardPipelineProfile>();

            try
            {
                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Does.Contain("not approved"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedProfileWithPlaceholderValues_IsRejected()
        {
            const string json = @"{
                ""researchConfigurationApproved"": true
            }";
            var asset = ScriptableObject.CreateInstance<RewardPipelineProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedValidProfile_CreatesImmutableRuntimeConfiguration()
        {
            const string json = @"{
                ""configurationId"": ""reward-pilot"",
                ""configurationVersion"": 2,
                ""researchConfigurationApproved"": true,
                ""baselineStandardDeviationMethod"": 0,
                ""minimumBaselineSamples"": 3,
                ""minimumBaselineStandardDeviation"": 0.01,
                ""trendWindowCount"": 5,
                ""minimumTrendSamples"": 3,
                ""settlingSeconds"": 5.0,
                ""maximumAttributionWaitSeconds"": 120.0,
                ""stressWeight"": 1.0,
                ""rmssdWeight"": 0.35,
                ""heartRateWeight"": 0.15,
                ""changePenaltyWeight"": 0.1,
                ""discomfortPenaltyWeight"": 2.0,
                ""safetyPenaltyWeight"": 2.0
            }";
            var asset = ScriptableObject.CreateInstance<RewardPipelineProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration, Is.Not.Null);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("reward-pilot"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(configuration.MinimumBaselineSamples, Is.EqualTo(3));
                Assert.That(configuration.TrendWindowCount, Is.EqualTo(5));
                Assert.That(configuration.SettlingSeconds, Is.EqualTo(5d));
                Assert.That(configuration.StressWeight, Is.EqualTo(1d));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
