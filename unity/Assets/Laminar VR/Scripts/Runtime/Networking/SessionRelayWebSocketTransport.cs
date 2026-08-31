using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public enum SessionRelayInboundRejectionReason
    {
        InvalidMessage,
        SessionMismatch,
        DuplicateMessage
    }

    public interface ISessionRelayMessageIdSource
    {
        string CreateMessageId();
    }

    public sealed class GuidSessionRelayMessageIdSource
        : ISessionRelayMessageIdSource
    {
        public string CreateMessageId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    public sealed class SessionRelayWebSocketTransport
        : ISessionRelayTransport
    {
        private const string ConnectFailureCode = "relay-connect-failed";
        private const string PairingInvalidCode = "relay-pairing-invalid";
        private const string PairingRejectedCode = "relay-pairing-rejected";
        private const string RemoteClosedCode = "relay-remote-closed";
        private const string ReceiveFailureCode = "relay-receive-failed";

        private readonly object synchronization = new object();
        private readonly SemaphoreSlim lifecycleGate =
            new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly SessionRelayConnectionInfo connectionInfo;
        private readonly SessionRelayJsonCodec codec;
        private readonly SessionRelayInboundMessageParser inboundParser;
        private readonly ISessionRelayWebSocketConnectionFactory
            connectionFactory;
        private readonly ISessionRelayMessageIdSource messageIdSource;
        private readonly HashSet<string> dispatchedMessageIds =
            new HashSet<string>(StringComparer.Ordinal);

        private SessionTransportConnectionState connectionState =
            SessionTransportConnectionState.Disconnected;
        private ISessionRelayWebSocketConnection activeConnection;
        private CancellationTokenSource connectionLifetime;
        private Task connectionMonitorTask = Task.CompletedTask;
        private string activeSessionId;

        public SessionRelayWebSocketTransport(
            SessionRelayConnectionInfo connectionInfo)
            : this(
                connectionInfo,
                new SessionRelayJsonCodec(
                    connectionInfo?.SchemaVersion
                        ?? throw new ArgumentNullException(
                            nameof(connectionInfo))),
                new SessionRelayInboundMessageParser(
                    connectionInfo.SchemaVersion),
                new ClientSessionRelayWebSocketConnectionFactory(),
                new GuidSessionRelayMessageIdSource())
        {
        }

        public SessionRelayWebSocketTransport(
            SessionRelayConnectionInfo connectionInfo,
            SessionRelayJsonCodec codec,
            SessionRelayInboundMessageParser inboundParser,
            ISessionRelayWebSocketConnectionFactory connectionFactory,
            ISessionRelayMessageIdSource messageIdSource)
        {
            this.connectionInfo = connectionInfo
                ?? throw new ArgumentNullException(nameof(connectionInfo));
            this.codec = codec
                ?? throw new ArgumentNullException(nameof(codec));
            this.inboundParser = inboundParser
                ?? throw new ArgumentNullException(nameof(inboundParser));
            this.connectionFactory = connectionFactory
                ?? throw new ArgumentNullException(nameof(connectionFactory));
            this.messageIdSource = messageIdSource
                ?? throw new ArgumentNullException(nameof(messageIdSource));

            if (!string.Equals(
                    connectionInfo.SchemaVersion,
                    codec.SchemaVersion,
                    StringComparison.Ordinal)
                || !string.Equals(
                    connectionInfo.SchemaVersion,
                    inboundParser.ExpectedSchemaVersion,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Relay connection, encoder, and parser schema versions must match.");
            }
        }

        public event Action<SessionRelayConfigurationMessage>
            SessionConfigurationReceived;

        // Physiology uses the independent Component B stream in this pilot.
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
            string> InboundMessageRejected;

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

        public string ActiveSessionId
        {
            get
            {
                lock (synchronization)
                {
                    return activeSessionId;
                }
            }
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            await lifecycleGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (ConnectionState
                    == SessionTransportConnectionState.Connected)
                {
                    return;
                }

                if (ConnectionState
                    != SessionTransportConnectionState.Disconnected)
                {
                    throw new InvalidOperationException(
                        "The session relay can connect only while disconnected.");
                }

                PublishTransition(
                    SessionTransportConnectionState.Connecting,
                    SessionTransportStatusReason.ConnectRequested);

                ISessionRelayWebSocketConnection candidate = null;
                try
                {
                    candidate = connectionFactory.Create()
                        ?? throw new InvalidOperationException(
                            "The session relay connection factory returned null.");
                    await candidate.ConnectAsync(
                            connectionInfo.Endpoint,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!candidate.IsOpen)
                    {
                        throw new InvalidOperationException(
                            "The relay connect completed without an open socket.");
                    }

                    var request = codec.SerializePairingRequest(
                        RequireGeneratedMessageId(),
                        connectionInfo.PairingCode,
                        connectionInfo.QuestClientId,
                        connectionInfo.AppVersion);
                    await candidate.SendTextAsync(request, cancellationToken)
                        .ConfigureAwait(false);

                    var pairingMessage = await candidate.ReceiveTextAsync(
                            connectionInfo.MaximumMessageBytes,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (pairingMessage.CloseReceived)
                    {
                        throw new InvalidOperationException(
                            "The relay closed before completing pairing.");
                    }

                    if (!codec.TryParsePairingResult(
                            pairingMessage.Text,
                            out var pairingResult,
                            out var pairingReason))
                    {
                        throw new SessionRelayConnectException(
                            PairingInvalidCode,
                            "The relay returned an invalid pairing result: "
                            + pairingReason
                            + ".");
                    }

                    if (!pairingResult.Accepted)
                    {
                        throw new SessionRelayConnectException(
                            PairingRejectedCode,
                            "The relay rejected pairing: "
                            + pairingResult.RejectionCode
                            + ".");
                    }

                    SetActiveSession(pairingResult.SessionId);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    candidate?.Abort();
                    candidate?.Dispose();
                    PublishTransition(
                        SessionTransportConnectionState.Disconnected,
                        SessionTransportStatusReason.OperationCancelled);
                    throw;
                }
                catch (SessionRelayConnectException exception)
                {
                    candidate?.Abort();
                    candidate?.Dispose();
                    PublishTransition(
                        SessionTransportConnectionState.Disconnected,
                        SessionTransportStatusReason.ConnectionFailed,
                        exception.DiagnosticCode);
                    throw;
                }
                catch
                {
                    candidate?.Abort();
                    candidate?.Dispose();
                    PublishTransition(
                        SessionTransportConnectionState.Disconnected,
                        SessionTransportStatusReason.ConnectionFailed,
                        ConnectFailureCode);
                    throw;
                }

                var lifetime = new CancellationTokenSource();
                lock (synchronization)
                {
                    activeConnection = candidate;
                    connectionLifetime = lifetime;
                }

                PublishTransition(
                    SessionTransportConnectionState.Connected,
                    SessionTransportStatusReason.ConnectSucceeded);
                var monitor = MonitorConnectionAsync(candidate, lifetime);
                lock (synchronization)
                {
                    connectionMonitorTask = monitor;
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public Task PublishQuestStateAsync(
            SessionRelayQuestState state,
            CancellationToken cancellationToken)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            EnsureActiveSession(state.SessionId);
            return SendAsync(
                codec.SerializeQuestState(state),
                cancellationToken);
        }

        public Task PublishTelemetryBatchAsync(
            IReadOnlyList<TelemetryEvent> telemetryEvents,
            CancellationToken cancellationToken)
        {
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

            var sessionId = ActiveSessionId;
            for (var index = 0; index < telemetryEvents.Count; index++)
            {
                var telemetryEvent = telemetryEvents[index]
                    ?? throw new ArgumentException(
                        "A telemetry batch cannot contain null events.",
                        nameof(telemetryEvents));
                if (!string.Equals(
                        telemetryEvent.SessionId,
                        sessionId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Telemetry events must belong to the paired session.",
                        nameof(telemetryEvents));
                }
            }

            return SendAsync(
                codec.SerializeTelemetryBatch(
                    RequireGeneratedMessageId(),
                    telemetryEvents),
                cancellationToken);
        }

        public async Task DisconnectAsync(
            CancellationToken cancellationToken)
        {
            await lifecycleGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (ConnectionState
                    == SessionTransportConnectionState.Disconnected)
                {
                    return;
                }

                if (ConnectionState
                    != SessionTransportConnectionState.Connected)
                {
                    throw new InvalidOperationException(
                        "The session relay can disconnect only while connected.");
                }

                PublishTransition(
                    SessionTransportConnectionState.Disconnecting,
                    SessionTransportStatusReason.DisconnectRequested);

                ISessionRelayWebSocketConnection connection;
                CancellationTokenSource lifetime;
                Task monitor;
                lock (synchronization)
                {
                    connection = activeConnection;
                    lifetime = connectionLifetime;
                    monitor = connectionMonitorTask;
                }

                lifetime?.Cancel();
                connection?.Abort();
                if (monitor != null)
                {
                    await monitor.ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                PublishTransition(
                    SessionTransportConnectionState.Disconnected,
                    SessionTransportStatusReason.DisconnectSucceeded);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                if (ConnectionState
                    == SessionTransportConnectionState.Disconnecting)
                {
                    PublishTransition(
                        SessionTransportConnectionState.Disconnected,
                        SessionTransportStatusReason.OperationCancelled);
                }

                throw;
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        private async Task MonitorConnectionAsync(
            ISessionRelayWebSocketConnection connection,
            CancellationTokenSource lifetime)
        {
            var diagnosticCode = RemoteClosedCode;
            try
            {
                await ReceiveLoopAsync(connection, lifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (lifetime.IsCancellationRequested)
            {
                diagnosticCode = null;
            }
            catch
            {
                diagnosticCode = ReceiveFailureCode;
            }
            finally
            {
                lifetime.Cancel();
                connection.Abort();
                connection.Dispose();
                lifetime.Dispose();

                SessionTransportStatus? lossStatus = null;
                lock (synchronization)
                {
                    if (ReferenceEquals(activeConnection, connection))
                    {
                        activeConnection = null;
                        connectionLifetime = null;
                        connectionMonitorTask = Task.CompletedTask;
                    }

                    if (connectionState
                        == SessionTransportConnectionState.Connected)
                    {
                        lossStatus = new SessionTransportStatus(
                            SessionTransportConnectionState.Connected,
                            SessionTransportConnectionState.Disconnected,
                            SessionTransportStatusReason.ConnectionLost,
                            diagnosticCode ?? RemoteClosedCode);
                        connectionState =
                            SessionTransportConnectionState.Disconnected;
                    }
                }

                if (lossStatus.HasValue)
                {
                    StatusChanged?.Invoke(lossStatus.Value);
                }
            }
        }

        private async Task ReceiveLoopAsync(
            ISessionRelayWebSocketConnection connection,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested
                && connection.IsOpen)
            {
                var message = await connection.ReceiveTextAsync(
                        connectionInfo.MaximumMessageBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (message.CloseReceived)
                {
                    return;
                }

                if (!inboundParser.TryParse(
                        message.Text,
                        out var inbound,
                        out var parseReason))
                {
                    InboundMessageRejected?.Invoke(
                        SessionRelayInboundRejectionReason.InvalidMessage,
                        parseReason.ToString());
                    continue;
                }

                DispatchInbound(inbound);
            }
        }

        private void DispatchInbound(SessionRelayInboundMessage inbound)
        {
            var sessionId = inbound.Kind
                == SessionRelayInboundMessageKind.SessionConfiguration
                ? inbound.Configuration.SessionId
                : inbound.Command.SessionId;
            if (!string.Equals(
                    sessionId,
                    ActiveSessionId,
                    StringComparison.Ordinal))
            {
                InboundMessageRejected?.Invoke(
                    SessionRelayInboundRejectionReason.SessionMismatch,
                    "session-mismatch");
                return;
            }

            var messageId = inbound.Kind
                == SessionRelayInboundMessageKind.SessionConfiguration
                ? inbound.Configuration.MessageId
                : inbound.Command.MessageId;
            bool isDuplicate;
            lock (synchronization)
            {
                isDuplicate = !dispatchedMessageIds.Add(messageId);
            }

            if (isDuplicate)
            {
                InboundMessageRejected?.Invoke(
                    SessionRelayInboundRejectionReason.DuplicateMessage,
                    "duplicate-message");
                return;
            }

            if (inbound.Kind
                == SessionRelayInboundMessageKind.SessionConfiguration)
            {
                SessionConfigurationReceived?.Invoke(inbound.Configuration);
            }
            else
            {
                SessionCommandReceived?.Invoke(inbound.Command);
            }
        }

        private async Task SendAsync(
            string json,
            CancellationToken cancellationToken)
        {
            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ISessionRelayWebSocketConnection connection;
                lock (synchronization)
                {
                    if (connectionState
                        != SessionTransportConnectionState.Connected
                        || activeConnection == null
                        || !activeConnection.IsOpen)
                    {
                        throw new InvalidOperationException(
                            "The session relay transport is not connected.");
                    }

                    connection = activeConnection;
                }

                await connection.SendTextAsync(json, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                sendGate.Release();
            }
        }

        private void SetActiveSession(string sessionId)
        {
            lock (synchronization)
            {
                if (!string.Equals(
                        activeSessionId,
                        sessionId,
                        StringComparison.Ordinal))
                {
                    dispatchedMessageIds.Clear();
                }

                activeSessionId = sessionId;
            }
        }

        private void EnsureActiveSession(string sessionId)
        {
            if (!string.Equals(
                    sessionId,
                    ActiveSessionId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The message does not belong to the paired session.",
                    nameof(sessionId));
            }
        }

        private string RequireGeneratedMessageId()
        {
            var messageId = messageIdSource.CreateMessageId();
            if (string.IsNullOrWhiteSpace(messageId))
            {
                throw new InvalidOperationException(
                    "The relay message ID source returned an empty value.");
            }

            return messageId.Trim();
        }

        private void PublishTransition(
            SessionTransportConnectionState targetState,
            SessionTransportStatusReason reason,
            string diagnosticCode = null)
        {
            SessionTransportStatus status;
            lock (synchronization)
            {
                var previousState = connectionState;
                connectionState = targetState;
                status = new SessionTransportStatus(
                    previousState,
                    targetState,
                    reason,
                    diagnosticCode);
            }

            StatusChanged?.Invoke(status);
        }

        private sealed class SessionRelayConnectException : Exception
        {
            public SessionRelayConnectException(
                string diagnosticCode,
                string message)
                : base(message)
            {
                DiagnosticCode = diagnosticCode;
            }

            public string DiagnosticCode { get; }
        }
    }
}
