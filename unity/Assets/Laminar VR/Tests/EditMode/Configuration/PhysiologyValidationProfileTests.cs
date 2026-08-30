using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class PhysiologyValidationProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<PhysiologyValidationProfile>();

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
                ""configurationId"": ""physiology-pilot"",
                ""configurationVersion"": 1,
                ""researchConfigurationApproved"": true
            }";
            var asset = ScriptableObject.CreateInstance<PhysiologyValidationProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Does.Contain("greater than 0"));
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
                ""configurationId"": ""physiology-pilot"",
                ""configurationVersion"": 2,
                ""researchConfigurationApproved"": true,
                ""staleAfterSeconds"": 90.0,
                ""minimumWindowDurationSeconds"": 30.0,
                ""maximumFutureClockSkewSeconds"": 2.0,
                ""sourceTimestampToleranceSeconds"": 0.001,
                ""probabilitySumTolerance"": 0.005,
                ""minimumDecisionSignalQuality"": 0.8,
                ""minimumRewardSignalQuality"": 0.9,
                ""maximumBufferedWindows"": 8
            }";
            var asset = ScriptableObject.CreateInstance<PhysiologyValidationProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration, Is.Not.Null);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("physiology-pilot"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(configuration.StaleAfterSeconds, Is.EqualTo(90d));
                Assert.That(configuration.MinimumDecisionSignalQuality, Is.EqualTo(0.8d).Within(0.00001d));
                Assert.That(configuration.MaximumBufferedWindows, Is.EqualTo(8));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}

