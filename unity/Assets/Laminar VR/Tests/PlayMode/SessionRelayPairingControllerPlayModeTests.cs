using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LaminarVR.AdaptiveMeditation.Tests.PlayMode
{
    public sealed class SessionRelayPairingControllerPlayModeTests
    {
        private readonly List<Object> createdObjects = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (var index = 0; index < createdObjects.Count; index++)
            {
                if (createdObjects[index] != null)
                {
                    Object.Destroy(createdObjects[index]);
                }
            }

            createdObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Pairing_ForwardsRuntimeCredentialsAndDisconnects()
        {
            var target = new RecordingConnectionTarget();
            var controller = CreateController(CreateApprovedProfile(), target);

            var pairTask = controller.PairAsync(
                "482913",
                "quest-install-7",
                CancellationToken.None);
            yield return AwaitTask(pairTask);

            Assert.That(
                target.ConnectionInfo.PairingCode,
                Is.EqualTo("482913"));
            Assert.That(
                target.ConnectionInfo.QuestClientId,
                Is.EqualTo("quest-install-7"));
            Assert.That(
                target.ConnectionInfo.AppVersion,
                Is.EqualTo(UnityEngine.Application.version.Trim()));
            Assert.That(
                controller.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Connected));
            Assert.That(controller.IsPairing, Is.False);
            Assert.That(controller.LastPairingError, Is.Empty);

            var disconnectTask = controller.DisconnectAsync(
                CancellationToken.None);
            yield return AwaitTask(disconnectTask);

            Assert.That(target.DisconnectCount, Is.EqualTo(1));
            Assert.That(
                controller.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
        }

        [UnityTest]
        public IEnumerator UnapprovedProfile_DoesNotReachConnectionTarget()
        {
            var profile = Track(ScriptableObject.CreateInstance<
                SessionRelayConnectionProfile>());
            var target = new RecordingConnectionTarget();
            var controller = CreateController(profile, target);

            var pairTask = controller.PairAsync(
                "482913",
                "quest-install-7",
                CancellationToken.None);
            yield return null;

            Assert.That(pairTask.IsFaulted, Is.True);
            Assert.Throws<System.InvalidOperationException>(
                () => pairTask.GetAwaiter().GetResult());
            Assert.That(target.ConnectCount, Is.Zero);
            Assert.That(controller.LastPairingError, Does.Contain("not approved"));
        }

        private SessionRelayPairingController CreateController(
            SessionRelayConnectionProfile profile,
            ISessionRelayConnectionTarget target)
        {
            var root = Track(new GameObject("SessionRelayPairingHarness"));
            var controller = root.AddComponent<
                SessionRelayPairingController>();
            controller.Configure(profile, target);
            return controller;
        }

        private SessionRelayConnectionProfile CreateApprovedProfile()
        {
            const string Json = @"{
                ""configurationId"": ""relay-test"",
                ""configurationVersion"": 1,
                ""deploymentConfigurationApproved"": true,
                ""relayEndpoint"": ""wss://relay.example.test/session"",
                ""schemaVersion"": ""relay-test-v1"",
                ""maximumMessageBytes"": 65536,
                ""maximumTelemetryEventsPerBatch"": 32
            }";
            var profile = Track(ScriptableObject.CreateInstance<
                SessionRelayConnectionProfile>());
            JsonUtility.FromJsonOverwrite(Json, profile);
            return profile;
        }

        private T Track<T>(T value) where T : Object
        {
            createdObjects.Add(value);
            return value;
        }

        private static IEnumerator AwaitTask(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            task.GetAwaiter().GetResult();
        }

        private sealed class RecordingConnectionTarget
            : ISessionRelayConnectionTarget
        {
            public int ConnectCount { get; private set; }

            public int DisconnectCount { get; private set; }

            public SessionRelayConnectionInfo ConnectionInfo { get; private set; }

            public SessionTransportConnectionState ConnectionState
            {
                get;
                private set;
            }

            public Task ConnectAsync(
                SessionRelayConnectionInfo connectionInfo,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConnectCount++;
                ConnectionInfo = connectionInfo;
                ConnectionState = SessionTransportConnectionState.Connected;
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DisconnectCount++;
                ConnectionState = SessionTransportConnectionState.Disconnected;
                return Task.CompletedTask;
            }
        }
    }
}
