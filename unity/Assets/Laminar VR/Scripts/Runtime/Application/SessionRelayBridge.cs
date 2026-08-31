using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Telemetry;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu(
        "Adaptive Meditation/Application/Session Relay Bridge")]
    [DisallowMultipleComponent]
    public sealed class SessionRelayBridge : MonoBehaviour,
        ISessionRelayConnectionTarget
    {
        private const int DefaultMaximumMessagesPerFrame = 64;

        [Header("Composition Root")]
        [SerializeField]
        private ApplicationBootstrap applicationBootstrap = null;

        [SerializeField]
        private ProductionSessionCoordinator productionCoordinator = null;

        [SerializeField]
        private VisualSessionBoundary visualSessionBoundary = null;

        [Header("Main-Thread Dispatch")]
        [SerializeField, Min(1)]
        private int maximumMessagesPerFrame = DefaultMaximumMessagesPerFrame;

        private readonly object transportSynchronization = new object();
        private readonly SemaphoreSlim lifecycleGate =
            new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<InboundRelayEvent> inboundEvents =
            new ConcurrentQueue<InboundRelayEvent>();
        private readonly ConcurrentQueue<SessionRelayQuestState>
            pendingQuestStates =
                new ConcurrentQueue<SessionRelayQuestState>();

        private ISessionRelayTransportFactory transportFactory;
        private IRecordedTelemetrySource recordedTelemetrySource;
        private ISessionRelayTransport activeTransport;
        private CancellationTokenSource componentLifetime;
        private Task activeQuestStatePublish;
        private Task activeTelemetryPublish;
        private TelemetryEvent[] activeTelemetryBatch;
        private int maximumTelemetryEventsPerBatch;
        private bool telemetryPublishBlocked;
        private bool coordinatorSubscribed;
        private string acceptedSessionId;

        public string LastValidationError { get; private set; } = string.Empty;

        public string LastConnectionError { get; private set; } = string.Empty;

        public string LastOutboundError { get; private set; } = string.Empty;

        public string LastTelemetryError { get; private set; } = string.Empty;

        public int PendingTelemetryEventCount =>
            (activeTelemetryBatch?.Length ?? 0)
            + (ResolveTelemetrySource()?.PendingEventCount ?? 0);

        public int RejectedInboundMessageCount { get; private set; }

        public string LastInboundRejection { get; private set; } = string.Empty;

        public SessionTransportConnectionState ConnectionState =>
            GetActiveTransport()?.ConnectionState
            ?? SessionTransportConnectionState.Disconnected;

        private void OnEnable()
        {
            componentLifetime = new CancellationTokenSource();
            SubscribeToCoordinator();
        }

        private void Update()
        {
            ProcessPendingMessages();
        }

        private void OnDisable()
        {
            UnsubscribeFromCoordinator();
            componentLifetime?.Cancel();
            BeginShutdown();
            componentLifetime?.Dispose();
            componentLifetime = null;
        }

        private void OnValidate()
        {
            maximumMessagesPerFrame = Math.Max(
                1,
                maximumMessagesPerFrame);
        }

        public void Configure(
            ApplicationBootstrap bootstrap,
            ProductionSessionCoordinator coordinator,
            VisualSessionBoundary boundary,
            ISessionRelayTransportFactory relayTransportFactory = null,
            IRecordedTelemetrySource telemetrySource = null)
        {
            if (GetActiveTransport() != null)
            {
                throw new InvalidOperationException(
                    "The session relay bridge cannot be reconfigured while a transport exists.");
            }

            UnsubscribeFromCoordinator();
            applicationBootstrap = bootstrap;
            productionCoordinator = coordinator;
            visualSessionBoundary = boundary;
            transportFactory = relayTransportFactory;
            recordedTelemetrySource = telemetrySource ?? coordinator;
            SubscribeToCoordinator();
        }

        public async Task ConnectAsync(
            SessionRelayConnectionInfo connectionInfo,
            CancellationToken cancellationToken)
        {
            if (connectionInfo == null)
            {
                throw new ArgumentNullException(nameof(connectionInfo));
            }

            if (!TryValidateBindings(out var validationError))
            {
                LastValidationError = validationError;
                throw new InvalidOperationException(validationError);
            }

            var lifetime = componentLifetime
                ?? throw new InvalidOperationException(
                    "The session relay bridge must be enabled before connecting.");

            maximumTelemetryEventsPerBatch =
                connectionInfo.MaximumTelemetryEventsPerBatch;

            await lifecycleGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (GetActiveTransport() != null)
                {
                    throw new InvalidOperationException(
                        "A session relay transport is already active.");
                }

                var factory = transportFactory
                    ?? new SessionRelayTransportFactory();
                var transport = factory.Create(connectionInfo)
                    ?? throw new InvalidOperationException(
                        "The session relay transport factory returned null.");
                AttachTransport(transport);
                lock (transportSynchronization)
                {
                    activeTransport = transport;
                }

                using (var linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        lifetime.Token))
                {
                    try
                    {
                        await transport.ConnectAsync(
                                linkedCancellation.Token)
                            .ConfigureAwait(false);
                        LastConnectionError = string.Empty;
                    }
                    catch (Exception exception)
                    {
                        ClearTransportIfCurrent(transport);
                        DetachTransport(transport);
                        LastConnectionError =
                            "relay-connect-failed:"
                            + exception.GetType().Name;
                        throw;
                    }
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async Task DisconnectAsync(
            CancellationToken cancellationToken)
        {
            await lifecycleGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var transport = TakeActiveTransport();
                if (transport == null)
                {
                    return;
                }

                try
                {
                    await transport.DisconnectAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    DetachTransport(transport);
                    acceptedSessionId = null;
                    ClearPendingQuestStates();
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public int ProcessPendingMessages()
        {
            ObserveQuestStatePublish();
            ObserveTelemetryPublish();

            var processedCount = 0;
            while (processedCount < maximumMessagesPerFrame
                && inboundEvents.TryDequeue(out var inboundEvent))
            {
                ProcessInboundEvent(inboundEvent);
                processedCount++;
            }

            TryStartQuestStatePublish();
            TryStartTelemetryPublish();
            return processedCount;
        }

        public bool TryValidateBindings(out string validationError)
        {
            if (applicationBootstrap == null)
            {
                validationError = "Assign an ApplicationBootstrap.";
                return false;
            }

            if (productionCoordinator == null)
            {
                validationError =
                    "Assign a ProductionSessionCoordinator.";
                return false;
            }

            if (visualSessionBoundary == null)
            {
                validationError = "Assign a VisualSessionBoundary.";
                return false;
            }

            validationError = string.Empty;
            LastValidationError = string.Empty;
            return true;
        }

        private void ProcessInboundEvent(InboundRelayEvent inboundEvent)
        {
            switch (inboundEvent.Kind)
            {
                case InboundRelayEventKind.Configuration:
                    ProcessConfiguration(inboundEvent.Configuration);
                    break;
                case InboundRelayEventKind.Command:
                    ProcessCommand(inboundEvent.Command);
                    break;
                case InboundRelayEventKind.Status:
                    visualSessionBoundary.ReceiveConnectionState(
                        inboundEvent.Status.CurrentState);
                    if (inboundEvent.Status.CurrentState
                        == SessionTransportConnectionState.Connected)
                    {
                        telemetryPublishBlocked = false;
                        QueueCurrentQuestState();
                    }

                    break;
                case InboundRelayEventKind.Rejection:
                    RejectInbound(
                        inboundEvent.RejectionReason
                        + ":"
                        + inboundEvent.DiagnosticCode);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ProcessConfiguration(
            SessionRelayConfigurationMessage configuration)
        {
            if (!applicationBootstrap.IsInitialized
                && !applicationBootstrap.TryInitialize(
                    out var bootstrapError))
            {
                RejectInbound("bootstrap-invalid:" + bootstrapError);
                return;
            }

            if (!string.Equals(
                    configuration.SceneId,
                    applicationBootstrap.SceneProfile.SceneId,
                    StringComparison.Ordinal))
            {
                RejectInbound("scene-id-mismatch");
                return;
            }

            if (acceptedSessionId != null
                && !string.Equals(
                    acceptedSessionId,
                    configuration.SessionId,
                    StringComparison.Ordinal))
            {
                RejectInbound("session-context-conflict");
                return;
            }

            acceptedSessionId = configuration.SessionId;
            visualSessionBoundary.ReceiveSessionContext(
                configuration.SessionId,
                configuration.ParticipantPseudonym,
                configuration.PreferredEnvironment);
        }

        private void ProcessCommand(SessionRelayCommandMessage command)
        {
            if (acceptedSessionId == null)
            {
                RejectInbound("session-configuration-required");
                return;
            }

            if (!string.Equals(
                    acceptedSessionId,
                    command.SessionId,
                    StringComparison.Ordinal))
            {
                RejectInbound("command-session-mismatch");
                return;
            }

            visualSessionBoundary.ReceiveCommand(
                command.MessageId,
                command.CommandType);
        }

        private void QueueCurrentQuestState()
        {
            var transport = GetActiveTransport();
            var sessionId = transport?.ActiveSessionId;
            if (transport == null
                || transport.ConnectionState
                    != SessionTransportConnectionState.Connected
                || string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            pendingQuestStates.Enqueue(
                new SessionRelayQuestState(
                    Guid.NewGuid().ToString("N"),
                    sessionId,
                    productionCoordinator.Phase,
                    UtcNowUnixSeconds()));
        }

        private void TryStartQuestStatePublish()
        {
            if (activeQuestStatePublish != null)
            {
                return;
            }

            var transport = GetActiveTransport();
            if (transport == null
                || transport.ConnectionState
                    != SessionTransportConnectionState.Connected
                || !pendingQuestStates.TryDequeue(out var state))
            {
                return;
            }

            try
            {
                activeQuestStatePublish = transport.PublishQuestStateAsync(
                    state,
                    componentLifetime.Token);
            }
            catch (Exception exception)
            {
                LastOutboundError =
                    "quest-state-publish-failed:"
                    + exception.GetType().Name;
                activeQuestStatePublish = null;
            }
        }

        private void ObserveQuestStatePublish()
        {
            if (activeQuestStatePublish == null
                || !activeQuestStatePublish.IsCompleted)
            {
                return;
            }

            try
            {
                activeQuestStatePublish.GetAwaiter().GetResult();
                LastOutboundError = string.Empty;
            }
            catch (OperationCanceledException)
            {
                LastOutboundError = "quest-state-publish-cancelled";
            }
            catch (Exception exception)
            {
                LastOutboundError =
                    "quest-state-publish-failed:"
                    + exception.GetType().Name;
            }

            activeQuestStatePublish = null;
        }

        private void TryStartTelemetryPublish()
        {
            if (activeTelemetryPublish != null
                || telemetryPublishBlocked
                || acceptedSessionId == null)
            {
                return;
            }

            var transport = GetActiveTransport();
            if (transport == null
                || transport.ConnectionState
                    != SessionTransportConnectionState.Connected)
            {
                return;
            }

            if (activeTelemetryBatch == null)
            {
                var telemetrySource = ResolveTelemetrySource();
                if (telemetrySource == null)
                {
                    return;
                }

                var batch = new List<TelemetryEvent>(
                    maximumTelemetryEventsPerBatch);
                while (batch.Count < maximumTelemetryEventsPerBatch
                    && telemetrySource.TryDequeue(
                        out var telemetryEvent))
                {
                    batch.Add(telemetryEvent);
                }

                if (batch.Count == 0)
                {
                    return;
                }

                activeTelemetryBatch = batch.ToArray();
            }

            try
            {
                activeTelemetryPublish = transport.PublishTelemetryBatchAsync(
                    activeTelemetryBatch,
                    componentLifetime.Token);
            }
            catch (Exception exception)
            {
                LastTelemetryError =
                    "telemetry-publish-failed:"
                    + exception.GetType().Name;
                telemetryPublishBlocked = true;
                activeTelemetryPublish = null;
            }
        }

        private void ObserveTelemetryPublish()
        {
            if (activeTelemetryPublish == null
                || !activeTelemetryPublish.IsCompleted)
            {
                return;
            }

            try
            {
                activeTelemetryPublish.GetAwaiter().GetResult();
                activeTelemetryBatch = null;
                LastTelemetryError = string.Empty;
            }
            catch (OperationCanceledException)
            {
                LastTelemetryError = "telemetry-publish-cancelled";
                telemetryPublishBlocked = true;
            }
            catch (Exception exception)
            {
                LastTelemetryError =
                    "telemetry-publish-failed:"
                    + exception.GetType().Name;
                telemetryPublishBlocked = true;
            }

            activeTelemetryPublish = null;
        }

        private IRecordedTelemetrySource ResolveTelemetrySource()
        {
            return recordedTelemetrySource ?? productionCoordinator;
        }

        private void HandleConfigurationReceived(
            SessionRelayConfigurationMessage configuration)
        {
            inboundEvents.Enqueue(
                InboundRelayEvent.ForConfiguration(configuration));
        }

        private void HandleCommandReceived(SessionRelayCommandMessage command)
        {
            inboundEvents.Enqueue(InboundRelayEvent.ForCommand(command));
        }

        private void HandleStatusChanged(SessionTransportStatus status)
        {
            inboundEvents.Enqueue(InboundRelayEvent.ForStatus(status));
        }

        private void HandleInboundMessageRejected(
            SessionRelayInboundRejectionReason reason,
            string diagnosticCode)
        {
            inboundEvents.Enqueue(
                InboundRelayEvent.ForRejection(reason, diagnosticCode));
        }

        private void HandlePhaseChanged(SessionPhaseTransition transition)
        {
            QueueCurrentQuestState();
        }

        private void RejectInbound(string reason)
        {
            RejectedInboundMessageCount++;
            LastInboundRejection = string.IsNullOrWhiteSpace(reason)
                ? "relay-message-rejected"
                : reason.Trim();
        }

        private void AttachTransport(ISessionRelayTransport transport)
        {
            transport.SessionConfigurationReceived +=
                HandleConfigurationReceived;
            transport.SessionCommandReceived += HandleCommandReceived;
            transport.StatusChanged += HandleStatusChanged;
            transport.InboundMessageRejected +=
                HandleInboundMessageRejected;
        }

        private void DetachTransport(ISessionRelayTransport transport)
        {
            transport.SessionConfigurationReceived -=
                HandleConfigurationReceived;
            transport.SessionCommandReceived -= HandleCommandReceived;
            transport.StatusChanged -= HandleStatusChanged;
            transport.InboundMessageRejected -=
                HandleInboundMessageRejected;
        }

        private void SubscribeToCoordinator()
        {
            if (!isActiveAndEnabled
                || coordinatorSubscribed
                || productionCoordinator == null)
            {
                return;
            }

            productionCoordinator.PhaseChanged += HandlePhaseChanged;
            coordinatorSubscribed = true;
        }

        private void UnsubscribeFromCoordinator()
        {
            if (!coordinatorSubscribed || productionCoordinator == null)
            {
                coordinatorSubscribed = false;
                return;
            }

            productionCoordinator.PhaseChanged -= HandlePhaseChanged;
            coordinatorSubscribed = false;
        }

        private void BeginShutdown()
        {
            var transport = TakeActiveTransport();
            if (transport == null)
            {
                return;
            }

            DetachTransport(transport);
            acceptedSessionId = null;
            ClearPendingQuestStates();
            visualSessionBoundary?.ReceiveConnectionState(
                SessionTransportConnectionState.Disconnected);

            try
            {
                var shutdownTask = transport.DisconnectAsync(
                    CancellationToken.None);
                ObserveInBackground(shutdownTask);
            }
            catch (Exception exception)
            {
                LastConnectionError =
                    "relay-disconnect-failed:"
                    + exception.GetType().Name;
            }
        }

        private void ClearPendingQuestStates()
        {
            while (pendingQuestStates.TryDequeue(out _))
            {
            }
        }

        private ISessionRelayTransport GetActiveTransport()
        {
            lock (transportSynchronization)
            {
                return activeTransport;
            }
        }

        private ISessionRelayTransport TakeActiveTransport()
        {
            lock (transportSynchronization)
            {
                var transport = activeTransport;
                activeTransport = null;
                return transport;
            }
        }

        private void ClearTransportIfCurrent(
            ISessionRelayTransport transport)
        {
            lock (transportSynchronization)
            {
                if (ReferenceEquals(activeTransport, transport))
                {
                    activeTransport = null;
                }
            }
        }

        private static void ObserveInBackground(Task task)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(
                completed =>
                {
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static double UtcNowUnixSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        }

        private enum InboundRelayEventKind
        {
            Configuration,
            Command,
            Status,
            Rejection
        }

        private readonly struct InboundRelayEvent
        {
            private InboundRelayEvent(
                InboundRelayEventKind kind,
                SessionRelayConfigurationMessage configuration,
                SessionRelayCommandMessage command,
                SessionTransportStatus status,
                SessionRelayInboundRejectionReason rejectionReason,
                string diagnosticCode)
            {
                Kind = kind;
                Configuration = configuration;
                Command = command;
                Status = status;
                RejectionReason = rejectionReason;
                DiagnosticCode = diagnosticCode;
            }

            public InboundRelayEventKind Kind { get; }

            public SessionRelayConfigurationMessage Configuration { get; }

            public SessionRelayCommandMessage Command { get; }

            public SessionTransportStatus Status { get; }

            public SessionRelayInboundRejectionReason RejectionReason { get; }

            public string DiagnosticCode { get; }

            public static InboundRelayEvent ForConfiguration(
                SessionRelayConfigurationMessage configuration)
            {
                return new InboundRelayEvent(
                    InboundRelayEventKind.Configuration,
                    configuration,
                    null,
                    default,
                    default,
                    null);
            }

            public static InboundRelayEvent ForCommand(
                SessionRelayCommandMessage command)
            {
                return new InboundRelayEvent(
                    InboundRelayEventKind.Command,
                    null,
                    command,
                    default,
                    default,
                    null);
            }

            public static InboundRelayEvent ForStatus(
                SessionTransportStatus status)
            {
                return new InboundRelayEvent(
                    InboundRelayEventKind.Status,
                    null,
                    null,
                    status,
                    default,
                    null);
            }

            public static InboundRelayEvent ForRejection(
                SessionRelayInboundRejectionReason reason,
                string diagnosticCode)
            {
                return new InboundRelayEvent(
                    InboundRelayEventKind.Rejection,
                    null,
                    null,
                    default,
                    reason,
                    diagnosticCode);
            }
        }
    }
}
