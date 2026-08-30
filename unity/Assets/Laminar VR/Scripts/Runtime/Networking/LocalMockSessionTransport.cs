using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public sealed class LocalMockSessionTransport<
        TSessionConfiguration,
        TSessionCommand,
        TQuestState> : ISessionTransport<
            TSessionConfiguration,
            TSessionCommand,
            TQuestState>
    {
        private readonly object synchronization = new object();
        private readonly List<TQuestState> publishedQuestStates =
            new List<TQuestState>();
        private readonly List<TelemetryEvent[]> publishedTelemetryBatches =
            new List<TelemetryEvent[]>();
        private SessionTransportConnectionState connectionState =
            SessionTransportConnectionState.Disconnected;
        private readonly Queue<string> connectFailureCodes =
            new Queue<string>();

        public event Action<TSessionConfiguration> SessionConfigurationReceived;

        public event Action<PhysiologyWindow> PhysiologyReceived;

        public event Action<TSessionCommand> SessionCommandReceived;

        public event Action<SessionTransportStatus> StatusChanged;

        public SessionTransportConnectionState ConnectionState
        {
            get
            {
                lock (synchronization)
                {
                    return connectionState;
                }
            }
        }

        public int PublishedQuestStateCount
        {
            get
            {
                lock (synchronization)
                {
                    return publishedQuestStates.Count;
                }
            }
        }

        public int PublishedTelemetryBatchCount
        {
            get
            {
                lock (synchronization)
                {
                    return publishedTelemetryBatches.Count;
                }
            }
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ConnectionState == SessionTransportConnectionState.Connected)
            {
                return Task.CompletedTask;
            }

            PublishTransition(
                SessionTransportConnectionState.Disconnected,
                SessionTransportConnectionState.Connecting,
                SessionTransportStatusReason.ConnectRequested);

            if (cancellationToken.IsCancellationRequested)
            {
                PublishTransition(
                    SessionTransportConnectionState.Connecting,
                    SessionTransportConnectionState.Disconnected,
                    SessionTransportStatusReason.OperationCancelled);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var failureCode = ConsumeNextConnectFailureCode();
            if (failureCode != null)
            {
                PublishTransition(
                    SessionTransportConnectionState.Connecting,
                    SessionTransportConnectionState.Disconnected,
                    SessionTransportStatusReason.ConnectionFailed,
                    failureCode);
                throw new InvalidOperationException(
                    "The local mock rejected the connection: " + failureCode);
            }

            PublishTransition(
                SessionTransportConnectionState.Connecting,
                SessionTransportConnectionState.Connected,
                SessionTransportStatusReason.ConnectSucceeded);
            return Task.CompletedTask;
        }

        public Task PublishQuestStateAsync(
            TQuestState state,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureMessagePresent(state, nameof(state));
            lock (synchronization)
            {
                EnsureConnected();
                publishedQuestStates.Add(state);
            }

            return Task.CompletedTask;
        }

        public Task PublishTelemetryBatchAsync(
            IReadOnlyList<TelemetryEvent> telemetryEvents,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (telemetryEvents == null)
            {
                throw new ArgumentNullException(nameof(telemetryEvents));
            }

            if (telemetryEvents.Count == 0)
            {
                throw new ArgumentException(
                    "A telemetry batch must contain at least one event.",
                    nameof(telemetryEvents));
            }

            var copy = new TelemetryEvent[telemetryEvents.Count];
            for (var index = 0; index < telemetryEvents.Count; index++)
            {
                copy[index] = telemetryEvents[index]
                    ?? throw new ArgumentException(
                        "A telemetry batch cannot contain null events.",
                        nameof(telemetryEvents));
            }

            lock (synchronization)
            {
                EnsureConnected();
                publishedTelemetryBatches.Add(copy);
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ConnectionState == SessionTransportConnectionState.Disconnected)
            {
                return Task.CompletedTask;
            }

            PublishTransition(
                SessionTransportConnectionState.Connected,
                SessionTransportConnectionState.Disconnecting,
                SessionTransportStatusReason.DisconnectRequested);

            if (cancellationToken.IsCancellationRequested)
            {
                PublishTransition(
                    SessionTransportConnectionState.Disconnecting,
                    SessionTransportConnectionState.Connected,
                    SessionTransportStatusReason.OperationCancelled);
                cancellationToken.ThrowIfCancellationRequested();
            }

            PublishTransition(
                SessionTransportConnectionState.Disconnecting,
                SessionTransportConnectionState.Disconnected,
                SessionTransportStatusReason.DisconnectSucceeded);
            return Task.CompletedTask;
        }

        public void EmitSessionConfiguration(TSessionConfiguration configuration)
        {
            EnsureMessagePresent(configuration, nameof(configuration));
            Action<TSessionConfiguration> handler;
            lock (synchronization)
            {
                EnsureConnected();
                handler = SessionConfigurationReceived;
            }

            handler?.Invoke(configuration);
        }

        public void EmitPhysiology(PhysiologyWindow physiology)
        {
            EnsureMessagePresent(physiology, nameof(physiology));
            Action<PhysiologyWindow> handler;
            lock (synchronization)
            {
                EnsureConnected();
                handler = PhysiologyReceived;
            }

            handler?.Invoke(physiology);
        }

        public void EmitSessionCommand(TSessionCommand command)
        {
            EnsureMessagePresent(command, nameof(command));
            Action<TSessionCommand> handler;
            lock (synchronization)
            {
                EnsureConnected();
                handler = SessionCommandReceived;
            }

            handler?.Invoke(command);
        }

        public bool SimulateConnectionLoss(string diagnosticCode = null)
        {
            SessionTransportStatus status;
            lock (synchronization)
            {
                if (connectionState
                    != SessionTransportConnectionState.Connected)
                {
                    return false;
                }

                status = SetState(
                    SessionTransportConnectionState.Connected,
                    SessionTransportConnectionState.Disconnected,
                    SessionTransportStatusReason.ConnectionLost,
                    diagnosticCode);
            }

            StatusChanged?.Invoke(status);
            return true;
        }

        public void FailNextConnect(string diagnosticCode)
        {
            if (string.IsNullOrWhiteSpace(diagnosticCode))
            {
                throw new ArgumentException(
                    "A diagnostic code is required.",
                    nameof(diagnosticCode));
            }

            lock (synchronization)
            {
                connectFailureCodes.Enqueue(diagnosticCode.Trim());
            }
        }

        public TQuestState GetPublishedQuestState(int index)
        {
            lock (synchronization)
            {
                return publishedQuestStates[index];
            }
        }

        public IReadOnlyList<TelemetryEvent> GetPublishedTelemetryBatch(int index)
        {
            lock (synchronization)
            {
                var source = publishedTelemetryBatches[index];
                var copy = new TelemetryEvent[source.Length];
                Array.Copy(source, copy, source.Length);
                return Array.AsReadOnly(copy);
            }
        }

        private void PublishTransition(
            SessionTransportConnectionState expectedState,
            SessionTransportConnectionState targetState,
            SessionTransportStatusReason reason,
            string diagnosticCode = null)
        {
            SessionTransportStatus status;
            lock (synchronization)
            {
                status = SetState(
                    expectedState,
                    targetState,
                    reason,
                    diagnosticCode);
            }

            StatusChanged?.Invoke(status);
        }

        private SessionTransportStatus SetState(
            SessionTransportConnectionState expectedState,
            SessionTransportConnectionState targetState,
            SessionTransportStatusReason reason,
            string diagnosticCode)
        {
            if (connectionState != expectedState)
            {
                throw new InvalidOperationException(
                    "Cannot move transport from "
                    + connectionState
                    + " when "
                    + expectedState
                    + " was required.");
            }

            var previousState = connectionState;
            connectionState = targetState;
            return new SessionTransportStatus(
                previousState,
                targetState,
                reason,
                diagnosticCode);
        }

        private string ConsumeNextConnectFailureCode()
        {
            lock (synchronization)
            {
                return connectFailureCodes.Count == 0
                    ? null
                    : connectFailureCodes.Dequeue();
            }
        }

        private void EnsureConnected()
        {
            if (connectionState != SessionTransportConnectionState.Connected)
            {
                throw new InvalidOperationException(
                    "The session transport is not connected.");
            }
        }

        private static void EnsureMessagePresent<TMessage>(
            TMessage message,
            string parameterName)
        {
            if (ReferenceEquals(message, null))
            {
                throw new ArgumentNullException(parameterName);
            }
        }
    }
}
