using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class ComponentBStreamConnectionProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilDeploymentIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<
                ComponentBStreamConnectionProfile>();
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
        public void ApprovedInvalidEndpoint_IsRejected()
        {
            const string Json = @"{
                ""configurationId"": ""component-b-dev"",
                ""configurationVersion"": 1,
                ""deploymentConfigurationApproved"": true,
                ""streamEndpoint"": ""http://localhost:8000/stream"",
                ""keepaliveIntervalSeconds"": 20.0,
                ""maximumMessageBytes"": 65536
            }";
            var asset = ScriptableObject.CreateInstance<
                ComponentBStreamConnectionProfile>();
            try
            {
                JsonUtility.FromJsonOverwrite(Json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Does.Contain("ws:// or wss://"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedProfile_CreatesImmutableRuntimeConfiguration()
        {
            const string Json = @"{
                ""configurationId"": ""component-b-dev"",
                ""configurationVersion"": 2,
                ""deploymentConfigurationApproved"": true,
                ""streamEndpoint"": ""ws://192.0.2.10:8000/stream"",
                ""keepaliveIntervalSeconds"": 20.0,
                ""maximumMessageBytes"": 65536
            }";
            var asset = ScriptableObject.CreateInstance<
                ComponentBStreamConnectionProfile>();
            try
            {
                JsonUtility.FromJsonOverwrite(Json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("component-b-dev"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(
                    configuration.StreamEndpoint.AbsoluteUri,
                    Is.EqualTo("ws://192.0.2.10:8000/stream"));
                Assert.That(configuration.KeepaliveIntervalSeconds, Is.EqualTo(20d));
                Assert.That(configuration.MaximumMessageBytes, Is.EqualTo(65536));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
