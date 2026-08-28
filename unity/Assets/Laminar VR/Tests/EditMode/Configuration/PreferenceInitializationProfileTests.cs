using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class PreferenceInitializationProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<PreferenceInitializationProfile>();

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
        public void ApprovedProfileWithMissingIdentity_IsRejected()
        {
            const string json = @"{
                ""researchConfigurationApproved"": true,
                ""preferenceWeight"": 0.5
            }";
            var asset = ScriptableObject.CreateInstance<PreferenceInitializationProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Does.Contain("ID is required"));
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
                ""configurationId"": ""preference-pilot"",
                ""configurationVersion"": 3,
                ""researchConfigurationApproved"": true,
                ""preferenceWeight"": 0.65
            }";
            var asset = ScriptableObject.CreateInstance<PreferenceInitializationProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration, Is.Not.Null);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("preference-pilot"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(3));
                Assert.That(
                    configuration.PreferenceWeight,
                    Is.EqualTo(0.65d).Within(0.00001d));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
