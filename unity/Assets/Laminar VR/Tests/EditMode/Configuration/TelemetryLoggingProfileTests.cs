using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class TelemetryLoggingProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<TelemetryLoggingProfile>();

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
            var asset = ScriptableObject.CreateInstance<TelemetryLoggingProfile>();

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
                ""configurationId"": ""telemetry-pilot"",
                ""configurationVersion"": 2,
                ""researchConfigurationApproved"": true,
                ""eventSchemaId"": ""adaptive-vr-telemetry"",
                ""eventSchemaVersion"": ""0.1-draft"",
                ""flushEveryEventCount"": 8
            }";
            var asset = ScriptableObject.CreateInstance<TelemetryLoggingProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration, Is.Not.Null);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("telemetry-pilot"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(
                    configuration.EventSchemaId,
                    Is.EqualTo("adaptive-vr-telemetry"));
                Assert.That(configuration.EventSchemaVersion, Is.EqualTo("0.1-draft"));
                Assert.That(configuration.FlushEveryEventCount, Is.EqualTo(8));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
