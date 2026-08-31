using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LaminarVR.AdaptiveMeditation.Tests.PlayMode
{
    public sealed class SessionRelayBridgePlayModeTests
    {
        private const string SchemaVersion = "mindsync-relay-test-v1";

        private readonly List<UnityEngine.Object> createdObjects =
            new List<UnityEngine.Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (var index = 0; index < createdObjects.Count; index++)
            {
                if (createdObjects[index] != null)
                {
                    UnityEngine.Object.Destroy(createdObjects[index]);
                }
            }

            createdObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Bridge_RoutesPairedConfigurationAndCommand()
        {
            var setup = CreateSetup();
            var connectTask = setup.Bridge.ConnectAsync(
                CreateConnectionInfo(),
                CancellationToken.None);
            yield return AwaitTask(connectTask);

            setup.Transport.EmitConfiguration(
                CreateConfiguration("production-test-scene"));
            setup.Transport.EmitCommand(
                new SessionRelayCommandMessage(
                    SchemaVersion,
                    "command-1",
                    "session-42",
                    SessionCommandType.Start));

            setup.Bridge.ProcessPendingMessages();
            Assert.That(setup.Boundary.PendingMessageCount, Is.EqualTo(4));
            Assert.That(setup.Bridge.RejectedInboundMessageCount, Is.Zero);
            Assert.That(setup.Transport.PublishedQuestStates, Has.Count.EqualTo(1));
            Assert.That(
                setup.Transport.PublishedQuestStates[0].SessionId,
                Is.EqualTo("session-42"));
            Assert.That(
                setup.Transport.PublishedQuestStates[0].Phase,
                Is.EqualTo(VrSessionPhase.Boot));
        }

        [UnityTest]
        public IEnumerator Bridge_RejectsWrongSceneAndCommandWithoutConfiguration()
        {
            var setup = CreateSetup();
            var connectTask = setup.Bridge.ConnectAsync(
                CreateConnectionInfo(),
                CancellationToken.None);
            yield return AwaitTask(connectTask);

            setup.Transport.EmitConfiguration(
                CreateConfiguration("another-scene"));
            setup.Transport.EmitCommand(
                new SessionRelayCommandMessage(
                    SchemaVersion,
                    "command-2",
                    "session-42",
                    SessionCommandType.Start));

            setup.Bridge.ProcessPendingMessages();
            Assert.That(setup.Boundary.PendingMessageCount, Is.EqualTo(2));
            Assert.That(
                setup.Bridge.RejectedInboundMessageCount,
                Is.EqualTo(2));
            Assert.That(
                setup.Bridge.LastInboundRejection,
                Is.EqualTo("session-configuration-required"));
        }

        [UnityTest]
        public IEnumerator DisablingBridge_DisconnectsWithoutPersistingPairingCode()
        {
            var setup = CreateSetup();
            var connectTask = setup.Bridge.ConnectAsync(
                CreateConnectionInfo(),
                CancellationToken.None);
            yield return AwaitTask(connectTask);

            var messagesBeforeDisable = setup.Boundary.PendingMessageCount;
            setup.Bridge.enabled = false;

            Assert.That(
                setup.Transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(
                setup.Boundary.PendingMessageCount,
                Is.EqualTo(messagesBeforeDisable + 1));
        }

        [UnityTest]
        public IEnumerator Bridge_PublishesDurablyRecordedTelemetryInBatches()
        {
            var setup = CreateSetup();
            var connectTask = setup.Bridge.ConnectAsync(
                CreateConnectionInfo(),
                CancellationToken.None);
            yield return AwaitTask(connectTask);

            setup.TelemetrySource.Enqueue(CreateTelemetryEvent(1));
            setup.TelemetrySource.Enqueue(CreateTelemetryEvent(2));
            setup.TelemetrySource.Enqueue(CreateTelemetryEvent(3));
            setup.Transport.EmitConfiguration(
                CreateConfiguration("production-test-scene"));

            setup.Bridge.ProcessPendingMessages();
            Assert.That(
                setup.Transport.PublishedTelemetryBatches,
                Has.Count.EqualTo(1));
            Assert.That(
                setup.Transport.PublishedTelemetryBatches[0],
                Has.Count.EqualTo(2));

            setup.Bridge.ProcessPendingMessages();
            Assert.That(
                setup.Transport.PublishedTelemetryBatches,
                Has.Count.EqualTo(2));
            Assert.That(
                setup.Transport.PublishedTelemetryBatches[1],
                Has.Count.EqualTo(1));

            setup.Bridge.ProcessPendingMessages();
            Assert.That(setup.Bridge.PendingTelemetryEventCount, Is.Zero);
            Assert.That(setup.Bridge.LastTelemetryError, Is.Empty);
        }

        [UnityTest]
        public IEnumerator FailedTelemetryPublish_RetainsBatchWithoutHotRetry()
        {
            var setup = CreateSetup();
            var connectTask = setup.Bridge.ConnectAsync(
                CreateConnectionInfo(),
                CancellationToken.None);
            yield return AwaitTask(connectTask);

            setup.Transport.FailTelemetryPublishes = true;
            setup.TelemetrySource.Enqueue(CreateTelemetryEvent(1));
            setup.Transport.EmitConfiguration(
                CreateConfiguration("production-test-scene"));

            setup.Bridge.ProcessPendingMessages();
            setup.Bridge.ProcessPendingMessages();
            setup.Bridge.ProcessPendingMessages();

            Assert.That(setup.Transport.TelemetryPublishAttemptCount, Is.EqualTo(1));
            Assert.That(setup.Bridge.PendingTelemetryEventCount, Is.EqualTo(1));
            Assert.That(
                setup.Bridge.LastTelemetryError,
                Is.EqualTo("telemetry-publish-failed:InvalidOperationException"));
        }

        private Setup CreateSetup()
        {
            var root = Track(new GameObject("SessionRelayBridgeHarness"));
            var adapter = root.AddComponent<RecordingAdapter>();
            var bootstrap = root.AddComponent<ApplicationBootstrap>();
            bootstrap.Configure(
                CreateSceneProfile(),
                adapter,
                StudyPolicyMode.StaticPersonalized);

            var coordinator = root.AddComponent<ProductionSessionCoordinator>();
            coordinator.enabled = false;
            var boundary = root.AddComponent<VisualSessionBoundary>();
            boundary.enabled = false;
            boundary.Configure(coordinator);

            var transport = new FakeSessionRelayTransport("session-42");
            var telemetrySource = new FakeRecordedTelemetrySource();
            var bridge = root.AddComponent<SessionRelayBridge>();
            bridge.enabled = false;
            bridge.Configure(
                bootstrap,
                coordinator,
                boundary,
                new FakeTransportFactory(transport),
                telemetrySource);
            bridge.enabled = true;

            return new Setup(
                bridge,
                boundary,
                transport,
                telemetrySource);
        }

        private SceneParameterProfile CreateSceneProfile()
        {
            var profile = ScriptableObject.CreateInstance<
                SceneParameterProfile>();
            JsonUtility.FromJsonOverwrite(SceneProfileJson, profile);
            return Track(profile);
        }

        private T Track<T>(T value) where T : UnityEngine.Object
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

        private static SessionRelayConnectionInfo CreateConnectionInfo()
        {
            return new SessionRelayConnectionInfo(
                new Uri("wss://relay.example.test/session"),
                SchemaVersion,
                "482913",
                "quest-install-7",
                "1.2.0",
                65536,
                2);
        }

        private static TelemetryEvent CreateTelemetryEvent(
            long sequenceNumber)
        {
            return new TelemetryEvent(
                "visual-event",
                "1",
                "telemetry-test",
                1,
                "event-" + sequenceNumber,
                sequenceNumber,
                "session-42",
                "P017",
                "session_phase_changed",
                1787282898.4d + sequenceNumber,
                sequenceNumber,
                true,
                Array.Empty<TelemetryField>());
        }

        private static SessionRelayConfigurationMessage CreateConfiguration(
            string sceneId)
        {
            return new SessionRelayConfigurationMessage(
                SchemaVersion,
                "configuration-1",
                "session-42",
                "P017",
                sceneId,
                new EnvironmentState(0.3f, 0.6f, 0.2f, 0.7f, 0.4f));
        }

        private sealed class Setup
        {
            public Setup(
                SessionRelayBridge bridge,
                VisualSessionBoundary boundary,
                FakeSessionRelayTransport transport,
                FakeRecordedTelemetrySource telemetrySource)
            {
                Bridge = bridge;
                Boundary = boundary;
                Transport = transport;
                TelemetrySource = telemetrySource;
            }

            public SessionRelayBridge Bridge { get; }

            public VisualSessionBoundary Boundary { get; }

            public FakeSessionRelayTransport Transport { get; }

            public FakeRecordedTelemetrySource TelemetrySource { get; }
        }

        private sealed class FakeRecordedTelemetrySource
            : IRecordedTelemetrySource
        {
            private readonly Queue<TelemetryEvent> events =
                new Queue<TelemetryEvent>();

            public int PendingEventCount => events.Count;

            public void Enqueue(TelemetryEvent telemetryEvent)
            {
                events.Enqueue(telemetryEvent);
            }

            public bool TryDequeue(out TelemetryEvent telemetryEvent)
            {
                if (events.Count == 0)
                {
                    telemetryEvent = null;
                    return false;
                }

                telemetryEvent = events.Dequeue();
                return true;
            }
        }

        private sealed class FakeTransportFactory
            : ISessionRelayTransportFactory
        {
            private readonly FakeSessionRelayTransport transport;

            public FakeTransportFactory(FakeSessionRelayTransport transport)
            {
                this.transport = transport;
            }

            public ISessionRelayTransport Create(
                SessionRelayConnectionInfo connectionInfo)
            {
                return transport;
            }
        }

        private sealed class FakeSessionRelayTransport
            : ISessionRelayTransport
        {
            public FakeSessionRelayTransport(string activeSessionId)
            {
                ActiveSessionId = activeSessionId;
            }

            public event Action<SessionRelayConfigurationMessage>
                SessionConfigurationReceived;

            public event Action<PhysiologyWindow> PhysiologyReceived
            {
                add { }
                remove { }
            }

            public event Action<SessionRelayCommandMessage>
                SessionCommandReceived;

            public event Action<SessionTransportStatus> StatusChanged;

            public event Action<
                SessionRelayInboundRejectionReason,
                string> InboundMessageRejected
            {
                add { }
                remove { }
            }

            public string ActiveSessionId { get; }

            public SessionTransportConnectionState ConnectionState
            {
                get;
                private set;
            }

            public List<SessionRelayQuestState> PublishedQuestStates
            {
                get;
            } = new List<SessionRelayQuestState>();

            public List<IReadOnlyList<TelemetryEvent>>
                PublishedTelemetryBatches { get; } =
                    new List<IReadOnlyList<TelemetryEvent>>();

            public bool FailTelemetryPublishes { get; set; }

            public int TelemetryPublishAttemptCount { get; private set; }

            public Task ConnectAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PublishTransition(
                    SessionTransportConnectionState.Connecting,
                    SessionTransportStatusReason.ConnectRequested);
                PublishTransition(
                    SessionTransportConnectionState.Connected,
                    SessionTransportStatusReason.ConnectSucceeded);
                return Task.CompletedTask;
            }

            public Task PublishQuestStateAsync(
                SessionRelayQuestState state,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PublishedQuestStates.Add(state);
                return Task.CompletedTask;
            }

            public Task PublishTelemetryBatchAsync(
                IReadOnlyList<TelemetryEvent> telemetryEvents,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TelemetryPublishAttemptCount++;
                if (FailTelemetryPublishes)
                {
                    return Task.FromException(
                        new InvalidOperationException(
                            "Simulated telemetry publish failure."));
                }

                var copy = new TelemetryEvent[telemetryEvents.Count];
                for (var index = 0; index < telemetryEvents.Count; index++)
                {
                    copy[index] = telemetryEvents[index];
                }

                PublishedTelemetryBatches.Add(Array.AsReadOnly(copy));
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ConnectionState
                    == SessionTransportConnectionState.Disconnected)
                {
                    return Task.CompletedTask;
                }

                PublishTransition(
                    SessionTransportConnectionState.Disconnecting,
                    SessionTransportStatusReason.DisconnectRequested);
                PublishTransition(
                    SessionTransportConnectionState.Disconnected,
                    SessionTransportStatusReason.DisconnectSucceeded);
                return Task.CompletedTask;
            }

            public void EmitConfiguration(
                SessionRelayConfigurationMessage configuration)
            {
                SessionConfigurationReceived?.Invoke(configuration);
            }

            public void EmitCommand(SessionRelayCommandMessage command)
            {
                SessionCommandReceived?.Invoke(command);
            }

            private void PublishTransition(
                SessionTransportConnectionState targetState,
                SessionTransportStatusReason reason)
            {
                var previousState = ConnectionState;
                ConnectionState = targetState;
                StatusChanged?.Invoke(
                    new SessionTransportStatus(
                        previousState,
                        targetState,
                        reason));
            }
        }

        private sealed class RecordingAdapter : MonoBehaviour,
            ISceneEnvironmentAdapter
        {
            public string SceneId => "production-test-scene";

            public SceneBindingValidation ValidateBindings()
            {
                return SceneBindingValidation.Succeeded();
            }

            public void ApplyState(EnvironmentState state)
            {
            }
        }

        private const string SceneProfileJson = @"{
            ""sceneId"": ""production-test-scene"",
            ""displayName"": ""Production Test Scene"",
            ""researchConfigurationApproved"": true,
            ""defaultIllumination"": 0.5,
            ""defaultWarmth"": 0.5,
            ""defaultAtmosphericSoftness"": 0.5,
            ""defaultColorRichness"": 0.5,
            ""defaultAmbientMotion"": 0.5,
            ""illuminationRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""warmthRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""atmosphericSoftnessRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""colorRichnessRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""ambientMotionRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""illuminationActionStep"": 0.1,
            ""warmthActionStep"": 0.1,
            ""atmosphericSoftnessActionStep"": 0.1,
            ""colorRichnessActionStep"": 0.1,
            ""ambientMotionActionStep"": 0.1,
            ""transitionDurationSeconds"": 0.05,
            ""minimumSecondsBetweenActions"": 0.0
        }";
    }
}
