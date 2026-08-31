using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class SessionRelayConnectionProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilDeploymentIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<
                SessionRelayConnectionProfile>();
            try
            {
                var created = asset.TryCreateConnectionInfo(
                    "482913",
                    "quest-install-7",
                    "1.2.0",
                    out var connectionInfo,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(connectionInfo, Is.Null);
                Assert.That(validationError, Does.Contain("not approved"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedInsecureEndpoint_RequiresDevelopmentOverride()
        {
            const string Json = @"{
                ""configurationId"": ""relay-dev"",
                ""configurationVersion"": 1,
                ""deploymentConfigurationApproved"": true,
                ""relayEndpoint"": ""ws://192.0.2.10:8080/session"",
                ""schemaVersion"": ""relay-test-v1"",
                ""maximumMessageBytes"": 65536
            }";
            var asset = CreateProfile(Json);
            try
            {
                var created = asset.TryCreateConnectionInfo(
                    "482913",
                    "quest-install-7",
                    "1.2.0",
                    out var connectionInfo,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(connectionInfo, Is.Null);
                Assert.That(
                    validationError,
                    Does.Contain("development-only override"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedSecureProfile_CreatesRuntimeOnlyConnectionInfo()
        {
            const string Json = @"{
                ""configurationId"": ""relay-pilot"",
                ""configurationVersion"": 3,
                ""deploymentConfigurationApproved"": true,
                ""relayEndpoint"": ""wss://relay.example.test/session"",
                ""schemaVersion"": ""relay-test-v1"",
                ""maximumMessageBytes"": 65536
            }";
            var asset = CreateProfile(Json);
            try
            {
                var created = asset.TryCreateConnectionInfo(
                    "482913",
                    "quest-install-7",
                    "1.2.0",
                    out var connectionInfo,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(asset.ConfigurationId, Is.EqualTo("relay-pilot"));
                Assert.That(asset.ConfigurationVersion, Is.EqualTo(3));
                Assert.That(
                    connectionInfo.Endpoint.AbsoluteUri,
                    Is.EqualTo("wss://relay.example.test/session"));
                Assert.That(
                    connectionInfo.SchemaVersion,
                    Is.EqualTo("relay-test-v1"));
                Assert.That(connectionInfo.PairingCode, Is.EqualTo("482913"));
                Assert.That(
                    connectionInfo.QuestClientId,
                    Is.EqualTo("quest-install-7"));
                Assert.That(connectionInfo.AppVersion, Is.EqualTo("1.2.0"));
                Assert.That(connectionInfo.MaximumMessageBytes, Is.EqualTo(65536));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private static SessionRelayConnectionProfile CreateProfile(
            string json)
        {
            var asset = ScriptableObject.CreateInstance<
                SessionRelayConnectionProfile>();
            JsonUtility.FromJsonOverwrite(json, asset);
            return asset;
        }
    }
}
