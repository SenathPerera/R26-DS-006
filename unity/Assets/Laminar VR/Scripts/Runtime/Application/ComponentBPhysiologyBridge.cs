using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using LaminarVR.AdaptiveMeditation.Session;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu(
        "Adaptive Meditation/Application/Component B Physiology Bridge")]
    [DisallowMultipleComponent]
    public sealed class ComponentBPhysiologyBridge : MonoBehaviour
    {
        [Header("Production Session")]
        [SerializeField]
        private ProductionSessionCoordinator productionCoordinator = null;

        [SerializeField]
        private VisualSessionBoundary visualSessionBoundary = null;

        [Header("Component B Connection")]
        [SerializeField]
        private ComponentBStreamConnectionProfile streamConnectionProfile = null;

        [SerializeField]
        private ReconnectBackoffProfile reconnectBackoffProfile = null;

        private readonly ConcurrentQueue<PhysiologyWindow> receivedWindows =
            new ConcurrentQueue<PhysiologyWindow>();
        private readonly ConcurrentQueue<SessionTransportStatus>
            receivedStatuses =
                new ConcurrentQueue<SessionTransportStatus>();
        private readonly ConcurrentQueue<
            ComponentBStressPayloadParseReasonCode> rejectedPayloads =
                new ConcurrentQueue<ComponentBStressPayloadParseReasonCode>();

        private CancellationTokenSource sessionCancellation;
        private ComponentBWebSocketPredictionSource physiologySource;
        private ConnectionReconnectController reconnectController;
        private NewestPhysiologyWindowForwardingGate forwardingGate;
        private Task<ConnectionOperationResult> activeConnectionOperation;
        private ConnectionOperationKind activeConnectionOperationKind;
        private VrSessionPhase observedPhase = VrSessionPhase.Boot;
        private bool coordinatorSubscribed;
        private bool reconnectRequested;
        private bool reconnectExhausted;

        public bool IsInitialized { get; private set; }

        public string LastValidationError { get; private set; } = string.Empty;

        public string LastConnectionError { get; private set; } = string.Empty;

        public int ForwardedWindowCount { get; private set; }

        public int BufferedWindowCount { get; private set; }

        public int DuplicateOrOutOfOrderWindowCount { get; private set; }

        public int InvalidWindowEndCount { get; private set; }

        public int RejectedPayloadCount { get; private set; }

        public ComponentBStressPayloadParseReasonCode LastPayloadRejectionReason
        {
            get;
            private set;
        }

        public SessionTransportConnectionState ConnectionState =>
            physiologySource?.ConnectionState
            ?? SessionTransportConnectionState.Disconnected;

        private void OnEnable()
        {
            sessionCancellation = new CancellationTokenSource();
            SubscribeToCoordinator();
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                if (!TryInitializeWhenSessionIsReady())
                {
                    return;
                }
            }

            ObserveCompletedConnectionOperation();
            DrainConnectionStatuses();
            ResetCompletedConnectionOperationKind();
            DrainRejectedPayloads();

            var monotonicTimeSeconds = Time.realtimeSinceStartupAsDouble;
            if (ShouldStreamForPhase(observedPhase))
            {
                DrainPhysiology(monotonicTimeSeconds);
                if (forwardingGate.TryFlush(
                        monotonicTimeSeconds,
                        out var flushedWindow))
                {
                    ForwardWindow(flushedWindow);
                }
            }
            else
            {
                ClearReceivedWindows();
            }

            EvaluateConnectionLifecycle();
        }

        private void OnDisable()
        {
            UnsubscribeFromCoordinator();
            BeginShutdown();
        }

        public void Configure(
            ProductionSessionCoordinator coordinator,
            VisualSessionBoundary boundary,
            ComponentBStreamConnectionProfile connectionProfile,
            ReconnectBackoffProfile backoffProfile)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Component B bridge cannot be reconfigured after initialization.");
            }

            UnsubscribeFromCoordinator();
            productionCoordinator = coordinator;
            visualSessionBoundary = boundary;
            streamConnectionProfile = connectionProfile;
            reconnectBackoffProfile = backoffProfile;
            SubscribeToCoordinator();
        }

        private bool TryInitializeWhenSessionIsReady()
        {
            if (productionCoordinator == null
                || visualSessionBoundary == null
                || streamConnectionProfile == null
                || reconnectBackoffProfile == null)
            {
                return FailInitialization(
                    "Assign the coordinator, visual session boundary, Component B "
                    + "stream profile, and reconnect profile.");
            }

            if (!productionCoordinator.IsInitialized)
            {
                return false;
            }

            if (!streamConnectionProfile.TryCreateRuntimeConfiguration(
                    out var streamConfiguration,
                    out var streamError))
            {
                return FailInitialization(streamError);
            }

            if (!reconnectBackoffProfile.TryCreateRuntimeConfiguration(
                    out var reconnectConfiguration,
                    out var reconnectError))
            {
                return FailInitialization(reconnectError);
            }

            var forwardingIntervalSeconds = productionCoordinator
                .ExpectedPhysiologyOutputIntervalSeconds;
            if (forwardingIntervalSeconds <= 0d)
            {
                return FailInitialization(
                    "The production coordinator has no valid physiology output interval.");
            }

            physiologySource = new ComponentBWebSocketPredictionSource(
                streamConfiguration.StreamEndpoint,
                streamConfiguration.KeepaliveIntervalSeconds,
                streamConfiguration.MaximumMessageBytes);
            physiologySource.PhysiologyReceived += HandlePhysiologyReceived;
            physiologySource.StatusChanged += HandleStatusChanged;
            physiologySource.PayloadRejected += HandlePayloadRejected;
            forwardingGate = new NewestPhysiologyWindowForwardingGate(
                forwardingIntervalSeconds);
            reconnectController = new ConnectionReconnectController(
                physiologySource,
                reconnectConfiguration,
                new TaskReconnectDelay());
            observedPhase = productionCoordinator.Phase;
            IsInitialized = true;
            LastValidationError = string.Empty;
            return true;
        }

        private void EvaluateConnectionLifecycle()
        {
            if (activeConnectionOperation != null)
            {
                return;
            }

            if (ShouldStreamForPhase(observedPhase))
            {
                if (ConnectionState
                        == SessionTransportConnectionState.Disconnected
                    && !reconnectExhausted)
                {
                    if (reconnectRequested)
                    {
                        StartReconnect();
                    }
                    else
                    {
                        StartConnect();
                    }
                }

                return;
            }

            reconnectRequested = false;
            reconnectExhausted = false;
            if (ConnectionState
                == SessionTransportConnectionState.Connected)
            {
                StartDisconnect();
            }
        }

        private void StartConnect()
        {
            activeConnectionOperationKind = ConnectionOperationKind.Connect;
            activeConnectionOperation = RunConnectAsync(
                sessionCancellation.Token);
        }

        private void StartReconnect()
        {
            reconnectRequested = false;
            activeConnectionOperationKind = ConnectionOperationKind.Reconnect;
            activeConnectionOperation = RunReconnectAsync(
                sessionCancellation.Token);
        }

        private void StartDisconnect()
        {
            activeConnectionOperationKind = ConnectionOperationKind.Disconnect;
            activeConnectionOperation = RunDisconnectAsync();
        }

        private async Task<ConnectionOperationResult> RunConnectAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                await physiologySource.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                return ConnectionOperationResult.Succeeded;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return ConnectionOperationResult.CancelledResult;
            }
            catch
            {
                return ConnectionOperationResult.Failed(
                    "Component B connection failed.");
            }
        }

        private async Task<ConnectionOperationResult> RunReconnectAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await reconnectController
                    .ReconnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                return result.Connected
                    ? ConnectionOperationResult.Succeeded
                    : ConnectionOperationResult.Exhausted;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return ConnectionOperationResult.CancelledResult;
            }
            catch
            {
                return ConnectionOperationResult.Failed(
                    "Component B reconnect failed.");
            }
        }

        private async Task<ConnectionOperationResult> RunDisconnectAsync()
        {
            try
            {
                await physiologySource.DisconnectAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                return ConnectionOperationResult.Succeeded;
            }
            catch
            {
                return ConnectionOperationResult.Failed(
                    "Component B disconnect failed.");
            }
        }

        private void ObserveCompletedConnectionOperation()
        {
            if (activeConnectionOperation == null
                || !activeConnectionOperation.IsCompleted)
            {
                return;
            }

            var result = activeConnectionOperation.GetAwaiter().GetResult();
            var completedKind = activeConnectionOperationKind;
            activeConnectionOperation = null;

            if (result.Success)
            {
                LastConnectionError = string.Empty;
                reconnectExhausted = false;
                return;
            }

            if (result.Cancelled)
            {
                return;
            }

            LastConnectionError = result.Error;
            if (completedKind == ConnectionOperationKind.Connect
                && ShouldStreamForPhase(observedPhase))
            {
                reconnectRequested = true;
            }
            else if (completedKind == ConnectionOperationKind.Reconnect
                && result.ReconnectExhausted)
            {
                reconnectExhausted = true;
            }
        }

        private void DrainConnectionStatuses()
        {
            while (receivedStatuses.TryDequeue(out var status))
            {
                visualSessionBoundary.ReceiveConnectionState(
                    status.CurrentState);
                if ((status.Reason
                        == SessionTransportStatusReason.ConnectionLost
                    || status.Reason
                        == SessionTransportStatusReason.ConnectionFailed)
                    && activeConnectionOperationKind
                        != ConnectionOperationKind.Reconnect
                    && ShouldStreamForPhase(observedPhase))
                {
                    reconnectRequested = true;
                    reconnectExhausted = false;
                }
                else if (status.CurrentState
                    == SessionTransportConnectionState.Connected)
                {
                    reconnectRequested = false;
                    reconnectExhausted = false;
                }
            }
        }

        private void DrainRejectedPayloads()
        {
            while (rejectedPayloads.TryDequeue(out var reason))
            {
                RejectedPayloadCount++;
                LastPayloadRejectionReason = reason;
            }
        }

        private void DrainPhysiology(double monotonicTimeSeconds)
        {
            while (receivedWindows.TryDequeue(out var window))
            {
                var result = forwardingGate.Observe(
                    window,
                    monotonicTimeSeconds,
                    out var forwardedWindow);
                switch (result)
                {
                    case PhysiologyWindowForwardingResult.Forwarded:
                        ForwardWindow(forwardedWindow);
                        break;
                    case PhysiologyWindowForwardingResult.Buffered:
                        BufferedWindowCount++;
                        break;
                    case PhysiologyWindowForwardingResult.DuplicateOrOutOfOrder:
                        DuplicateOrOutOfOrderWindowCount++;
                        break;
                    case PhysiologyWindowForwardingResult.WindowEndInvalid:
                        InvalidWindowEndCount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void ForwardWindow(PhysiologyWindow window)
        {
            visualSessionBoundary.ReceivePhysiology(window);
            ForwardedWindowCount++;
        }

        private void HandlePhysiologyReceived(PhysiologyWindow window)
        {
            receivedWindows.Enqueue(window);
        }

        private void HandleStatusChanged(SessionTransportStatus status)
        {
            receivedStatuses.Enqueue(status);
        }

        private void HandlePayloadRejected(
            ComponentBStressPayloadParseReasonCode reason)
        {
            rejectedPayloads.Enqueue(reason);
        }

        private void HandlePhaseChanged(SessionPhaseTransition transition)
        {
            observedPhase = transition.CurrentPhase;
            if (transition.CurrentPhase == VrSessionPhase.Completed
                || transition.CurrentPhase == VrSessionPhase.Aborted)
            {
                sessionCancellation?.Cancel();
            }
        }

        private void SubscribeToCoordinator()
        {
            if (coordinatorSubscribed || productionCoordinator == null)
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
                return;
            }

            productionCoordinator.PhaseChanged -= HandlePhaseChanged;
            coordinatorSubscribed = false;
        }

        private bool FailInitialization(string validationError)
        {
            LastValidationError = string.IsNullOrWhiteSpace(validationError)
                ? "Unknown Component B bridge validation error."
                : validationError.Trim();
            enabled = false;
            Debug.LogError(
                "[ComponentBPhysiologyBridge] initialization_failed reason="
                + LastValidationError,
                this);
            return false;
        }

        private void BeginShutdown()
        {
            var cancellationToDispose = sessionCancellation;
            cancellationToDispose?.Cancel();
            sessionCancellation = null;

            var sourceToShutdown = physiologySource;
            var operationToObserve = activeConnectionOperation;
            if (sourceToShutdown != null)
            {
                sourceToShutdown.PhysiologyReceived -= HandlePhysiologyReceived;
                sourceToShutdown.StatusChanged -= HandleStatusChanged;
                sourceToShutdown.PayloadRejected -= HandlePayloadRejected;
                _ = ShutdownSourceAsync(
                    sourceToShutdown,
                    operationToObserve,
                    cancellationToDispose);
            }
            else
            {
                cancellationToDispose?.Dispose();
            }

            physiologySource = null;
            reconnectController = null;
            forwardingGate = null;
            activeConnectionOperation = null;
            IsInitialized = false;
            ClearReceivedWindows();
            while (receivedStatuses.TryDequeue(out _))
            {
            }

            while (rejectedPayloads.TryDequeue(out _))
            {
            }
        }

        private static async Task ShutdownSourceAsync(
            IPhysiologyStreamSource source,
            Task operationToObserve,
            CancellationTokenSource cancellationToDispose)
        {
            try
            {
                if (operationToObserve != null)
                {
                    await operationToObserve.ConfigureAwait(false);
                }

                if (source.ConnectionState
                    == SessionTransportConnectionState.Connected)
                {
                    await source.DisconnectAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                cancellationToDispose?.Dispose();
            }
        }

        private void ResetCompletedConnectionOperationKind()
        {
            if (activeConnectionOperation == null)
            {
                activeConnectionOperationKind = ConnectionOperationKind.None;
            }
        }

        private void ClearReceivedWindows()
        {
            while (receivedWindows.TryDequeue(out _))
            {
            }
        }

        private static bool ShouldStreamForPhase(VrSessionPhase phase)
        {
            return phase == VrSessionPhase.Acclimatization
                || phase == VrSessionPhase.Adaptive
                || phase == VrSessionPhase.Paused
                || phase == VrSessionPhase.Stabilization;
        }

        private enum ConnectionOperationKind
        {
            None,
            Connect,
            Reconnect,
            Disconnect
        }

        private readonly struct ConnectionOperationResult
        {
            private ConnectionOperationResult(
                bool success,
                bool cancelled,
                bool reconnectExhausted,
                string error)
            {
                Success = success;
                Cancelled = cancelled;
                ReconnectExhausted = reconnectExhausted;
                Error = error;
            }

            public bool Success { get; }

            public bool Cancelled { get; }

            public bool ReconnectExhausted { get; }

            public string Error { get; }

            public static ConnectionOperationResult Succeeded =>
                new ConnectionOperationResult(true, false, false, string.Empty);

            public static ConnectionOperationResult CancelledResult =>
                new ConnectionOperationResult(false, true, false, string.Empty);

            public static ConnectionOperationResult Exhausted =>
                new ConnectionOperationResult(
                    false,
                    false,
                    true,
                    "Component B reconnect attempts were exhausted.");

            public static ConnectionOperationResult Failed(string error)
            {
                return new ConnectionOperationResult(
                    false,
                    false,
                    false,
                    error);
            }
        }
    }
}
