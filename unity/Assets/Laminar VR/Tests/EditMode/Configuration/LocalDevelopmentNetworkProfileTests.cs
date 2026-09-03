using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class LocalDevelopmentNetworkProfileTests
    {
        [Test]
        public void EmptyHost_IsRejectedWithConfigurationGuidance()
        {
            var profile = ScriptableObject.CreateInstance<
                LocalDevelopmentNetworkProfile>();
            try
            {
                var created = profile.TryGetLyriaHttpBaseUrl(
                    out var endpoint,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(endpoint, Is.Empty);
                Assert.That(
                    validationError,
                    Does.Contain("MINDSYNC_DEVELOPMENT_HOST"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ValidHost_BuildsAllLocalServiceEndpoints()
        {
            const string Json = @"{
                ""host"": ""192.0.2.25"",
                ""componentBPort"": 8000,
                ""lyriaBackendPort"": 8002,
                ""sessionRelayPort"": 8080
            }";
            var profile = CreateProfile(Json);
            try
            {
                Assert.That(
                    profile.TryGetComponentBStreamEndpoint(
                        out var componentB,
                        out var componentBError),
                    Is.True,
                    componentBError);
                Assert.That(
                    profile.TryGetLyriaHttpBaseUrl(
                        out var lyriaHttp,
                        out var lyriaHttpError),
                    Is.True,
                    lyriaHttpError);
                Assert.That(
                    profile.TryGetLyriaRealtimeWebsocketUrl(
                        out var lyriaRealtime,
                        out var lyriaRealtimeError),
                    Is.True,
                    lyriaRealtimeError);
                Assert.That(
                    profile.TryGetSessionRelayEndpoint(
                        out var relay,
                        out var relayError),
                    Is.True,
                    relayError);

                Assert.That(componentB, Is.EqualTo("ws://192.0.2.25:8000/stream"));
                Assert.That(lyriaHttp, Is.EqualTo("http://192.0.2.25:8002"));
                Assert.That(lyriaRealtime, Is.EqualTo("ws://192.0.2.25:8002/live-music"));
                Assert.That(relay, Is.EqualTo("ws://192.0.2.25:8080/realtime?role=quest"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void HostContainingUrlParts_IsRejected()
        {
            var profile = CreateProfile(@"{""host"": ""http://192.0.2.25:8002""}");
            try
            {
                var created = profile.TryGetLyriaHttpBaseUrl(
                    out _,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(validationError, Does.Contain("without a scheme"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static LocalDevelopmentNetworkProfile CreateProfile(string json)
        {
            var profile = ScriptableObject.CreateInstance<
                LocalDevelopmentNetworkProfile>();
            JsonUtility.FromJsonOverwrite(json, profile);
            return profile;
        }
    }
}
