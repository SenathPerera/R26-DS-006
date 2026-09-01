using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class LocalMockSessionTransportTests
    {
        [Test]
        public void Transport_ImplementsTypedSessionBoundary()
        {
            var transport = CreateTransport();

            Assert.That(
                transport,
                Is.InstanceOf<ISessionTransport<string, string, string>>());
            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
        }

        [Test]
        public void Lifecycle_CompletesWithoutAUnitySynchronizationContextYield()
        {
            var transport = CreateTransport();

            var connectTask = transport.ConnectAsync(CancellationToken.None);
            var disconnectTask = transport.DisconnectAsync(CancellationToken.None);

            Assert.That(connectTask.IsCompletedSuccessfully, Is.True);
            Assert.That(disconnectTask.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task ConnectAsync_EmitsOrderedTransitionsAndIsIdempotent()
        {
            var transport = CreateTransport();
            var statuses = new List<SessionTransportStatus>();
            transport.StatusChanged += statuses.Add;

            await transport.ConnectAsync(CancellationToken.None);
            await transport.ConnectAsync(CancellationToken.None);

            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Connected));
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(
                statuses[0].CurrentState,
                Is.EqualTo(SessionTransportConnectionState.Connecting));
            Assert.That(
                statuses[0].Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectRequested));
            Assert.That(
                statuses[1].CurrentState,
                Is.EqualTo(SessionTransportConnectionState.Connected));
            Assert.That(
                statuses[1].Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectSucceeded));
        }

        [Test]
        public void ConnectAsync_PreCancelledRequestDoesNotChangeState()
        {
            var transport = CreateTransport();
            var statuses = new List<SessionTransportStatus>();
            transport.StatusChanged += statuses.Add;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                Assert.CatchAsync<OperationCanceledException>(
                    async () => await transport.ConnectAsync(cancellation.Token));
            }

            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(statuses, Is.Empty);
        }

        [Test]
        public void ConnectAsync_CancellationDuringConnectRestoresDisconnectedState()
        {
            var transport = CreateTransport();
            var statuses = new List<SessionTransportStatus>();
            using (var cancellation = new CancellationTokenSource())
            {
                transport.StatusChanged += status =>
                {
                    statuses.Add(status);
                    if (status.CurrentState
                        == SessionTransportConnectionState.Connecting)
                    {
                        cancellation.Cancel();
                    }
                };

                Assert.CatchAsync<OperationCanceledException>(
                    async () => await transport.ConnectAsync(cancellation.Token));
            }

            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(
                statuses[1].Reason,
                Is.EqualTo(SessionTransportStatusReason.OperationCancelled));
        }

        [Test]
        public async Task FailedConnect_ReturnsToDisconnectedAndAllowsRetry()
        {
            var transport = CreateTransport();
            var statuses = new List<SessionTransportStatus>();
            transport.StatusChanged += statuses.Add;
            transport.FailNextConnect("synthetic-connect-failure");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await transport.ConnectAsync(CancellationToken.None));

            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(
                statuses[1].Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectionFailed));
            Assert.That(
                statuses[1].DiagnosticCode,
                Is.EqualTo("synthetic-connect-failure"));

            await transport.ConnectAsync(CancellationToken.None);
            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Connected));
        }

        [Test]
        public async Task InboundMessages_AreRoutedOnlyWhileConnected()
        {
            var transport = CreateTransport();
            string receivedConfiguration = null;
            PhysiologyWindow receivedPhysiology = null;
            string receivedCommand = null;
            var physiology = CreatePhysiologyWindow();
            transport.SessionConfigurationReceived +=
                value => receivedConfiguration = value;
            transport.PhysiologyReceived += value => receivedPhysiology = value;
            transport.SessionCommandReceived += value => receivedCommand = value;

            Assert.Throws<InvalidOperationException>(
                () => transport.EmitPhysiology(physiology));
            await transport.ConnectAsync(CancellationToken.None);

            transport.EmitSessionConfiguration("configuration");
            transport.EmitPhysiology(physiology);
            transport.EmitSessionCommand("command");

            Assert.That(receivedConfiguration, Is.EqualTo("configuration"));
            Assert.That(receivedPhysiology, Is.SameAs(physiology));
            Assert.That(receivedCommand, Is.EqualTo("command"));
            Assert.Throws<ArgumentNullException>(
                () => transport.EmitSessionCommand(null));
        }

        [Test]
        public async Task OutboundMessages_AreCapturedWithCopyIsolation()
        {
            var transport = CreateTransport();
            await transport.ConnectAsync(CancellationToken.None);
            var sourceBatch = new[] { CreateTelemetryEvent(1L) };

            await transport.PublishQuestStateAsync(
                "ready",
                CancellationToken.None);
            await transport.PublishTelemetryBatchAsync(
                sourceBatch,
                CancellationToken.None);
            sourceBatch[0] = CreateTelemetryEvent(99L);

            Assert.That(transport.PublishedQuestStateCount, Is.EqualTo(1));
            Assert.That(transport.GetPublishedQuestState(0), Is.EqualTo("ready"));
            Assert.That(transport.PublishedTelemetryBatchCount, Is.EqualTo(1));
            Assert.That(
                transport.GetPublishedTelemetryBatch(0)[0].SequenceNumber,
                Is.EqualTo(1L));
        }

        [Test]
        public async Task OutboundMessages_RejectInvalidInputAndDisconnectedUse()
        {
            var transport = CreateTransport();

            Assert.Throws<InvalidOperationException>(
                () => transport.PublishQuestStateAsync(
                    "state",
                    CancellationToken.None));
            await transport.ConnectAsync(CancellationToken.None);
            Assert.Throws<ArgumentNullException>(
                () => transport.PublishQuestStateAsync(
                    null,
                    CancellationToken.None));
            Assert.Throws<ArgumentNullException>(
                () => transport.PublishTelemetryBatchAsync(
                    null,
                    CancellationToken.None));
            Assert.Throws<ArgumentException>(
                () => transport.PublishTelemetryBatchAsync(
                    Array.Empty<TelemetryEvent>(),
                    CancellationToken.None));
            Assert.Throws<ArgumentException>(
                () => transport.PublishTelemetryBatchAsync(
                    new TelemetryEvent[] { null },
                    CancellationToken.None));
        }

        [Test]
        public async Task ConnectionLossFreezesTransportUntilExplicitReconnect()
        {
            var transport = CreateTransport();
            SessionTransportStatus lastStatus = default;
            transport.StatusChanged += status => lastStatus = status;
            await transport.ConnectAsync(CancellationToken.None);

            var changed = transport.SimulateConnectionLoss("mock-link-lost");

            Assert.That(changed, Is.True);
            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(
                lastStatus.Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectionLost));
            Assert.That(lastStatus.DiagnosticCode, Is.EqualTo("mock-link-lost"));
            Assert.Throws<InvalidOperationException>(
                () => transport.EmitPhysiology(CreatePhysiologyWindow()));
            Assert.Throws<InvalidOperationException>(
                () => transport.PublishQuestStateAsync(
                    "unsafe-update",
                    CancellationToken.None));
            Assert.That(transport.SimulateConnectionLoss(), Is.False);

            await transport.ConnectAsync(CancellationToken.None);
            transport.EmitPhysiology(CreatePhysiologyWindow());
            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Connected));
        }

        [Test]
        public async Task DisconnectAsync_EmitsOrderedTransitionsAndIsIdempotent()
        {
            var transport = CreateTransport();
            await transport.ConnectAsync(CancellationToken.None);
            var statuses = new List<SessionTransportStatus>();
            transport.StatusChanged += statuses.Add;

            await transport.DisconnectAsync(CancellationToken.None);
            await transport.DisconnectAsync(CancellationToken.None);

            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(
                statuses[0].Reason,
                Is.EqualTo(SessionTransportStatusReason.DisconnectRequested));
            Assert.That(
                statuses[1].Reason,
                Is.EqualTo(SessionTransportStatusReason.DisconnectSucceeded));
        }

        private static LocalMockSessionTransport<string, string, string>
            CreateTransport()
        {
            return new LocalMockSessionTransport<string, string, string>();
        }

        private static PhysiologyWindow CreatePhysiologyWindow()
        {
            return new PhysiologyWindow(
                1000d,
                940d,
                1000d,
                78d,
                34d,
                42d,
                new StressDecision(
                    StressDecisionMode.Point,
                    2,
                    null,
                    null,
                    "moderate",
                    0.5d,
                    false,
                    new StressProbabilityVector(0.1d, 0.2d, 0.6d, 0.1d),
                    1.7d),
                0.95d);
        }

        private static TelemetryEvent CreateTelemetryEvent(long sequenceNumber)
        {
            return new TelemetryEvent(
                "telemetry-schema",
                "test",
                "logging-test",
                1,
                "event-" + sequenceNumber,
                sequenceNumber,
                "session-1",
                "P017",
                TelemetryEventTypes.NetworkConnected,
                1000d + sequenceNumber,
                sequenceNumber,
                false,
                Array.Empty<TelemetryField>());
        }
    }
}
