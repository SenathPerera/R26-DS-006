using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class ReconnectBackoffProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<ReconnectBackoffProfile>();

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
            var asset = ScriptableObject.CreateInstance<ReconnectBackoffProfile>();

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
                ""configurationId"": ""reconnect-pilot"",
                ""configurationVersion"": 2,
                ""researchConfigurationApproved"": true,
                ""maximumAttempts"": 4,
                ""initialDelaySeconds"": 1.0,
                ""maximumDelaySeconds"": 8.0,
                ""delayMultiplier"": 2.0
            }";
            var asset = ScriptableObject.CreateInstance<ReconnectBackoffProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration, Is.Not.Null);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("reconnect-pilot"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(configuration.MaximumAttempts, Is.EqualTo(4));
                Assert.That(configuration.InitialDelaySeconds, Is.EqualTo(1d));
                Assert.That(configuration.MaximumDelaySeconds, Is.EqualTo(8d));
                Assert.That(configuration.DelayMultiplier, Is.EqualTo(2d));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
