using System;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public interface IComponentBKeepaliveDelay
    {
        Task DelayAsync(
            double delaySeconds,
            CancellationToken cancellationToken);
    }

    public sealed class TaskComponentBKeepaliveDelay
        : IComponentBKeepaliveDelay
    {
        public Task DelayAsync(
            double delaySeconds,
            CancellationToken cancellationToken)
        {
            return Task.Delay(
                TimeSpan.FromSeconds(delaySeconds),
                cancellationToken);
        }
    }

    public sealed class ComponentBWebSocketPredictionSource
        : IPhysiologyStreamSource
    {
        private const string KeepaliveText = "keepalive";
        private const string ConnectFailureCode =
            "component-b-connect-failed";
        private const string RemoteClosedCode =
            "component-b-remote-closed";
        private const string ReceiveFailureCode =
            "component-b-receive-failed";
        private const string KeepaliveFailureCode =
            "component-b-keepalive-failed";

        private readonly object synchronization = new object();
        private readonly SemaphoreSlim lifecycleGate =
            new SemaphoreSlim(1, 1);
        private readonly Uri streamEndpoint;
        private readonly double keepaliveIntervalSeconds;
        private readonly int maximumMessageBytes;
        private readonly ComponentBStressPayloadParser parser;
        private readonly IComponentBWebSocketConnectionFactory connectionFactory;
        private readonly IComponentBKeepaliveDelay keepaliveDelay;

        private SessionTransportConnectionState connectionState =
            SessionTransportConnectionState.Disconnected;
        private IComponentBWebSocketConnection activeConnection;
        private CancellationTokenSource connectionLifetime;
        private Task connectionMonitorTask = Task.CompletedTask;

        public ComponentBWebSocketPredictionSource(
            Uri streamEndpoint,
            double keepaliveIntervalSeconds,
            int maximumMessageBytes)
            : this(
                streamEndpoint,
                keepaliveIntervalSeconds,
                maximumMessageBytes,
                new ComponentBStressPayloadParser(),
                new ClientComponentBWebSocketConnectionFactory(),
                new TaskComponentBKeepaliveDelay())
        {
        }

        public ComponentBWebSocketPredictionSource(
            Uri streamEndpoint,
            double keepaliveIntervalSeconds,
            int maximumMessageBytes,
            ComponentBStressPayloadParser parser,
            IComponentBWebSocketConnectionFactory connectionFactory,
            IComponentBKeepaliveDelay keepaliveDelay)
        {
            ValidateEndpoint(streamEndpoint);
            if (double.IsNaN(keepaliveIntervalSeconds)
                || double.IsInfinity(keepaliveIntervalSeconds)
                || keepaliveIntervalSeconds <= 0d
                || keepaliveIntervalSeconds > TimeSpan.MaxValue.TotalSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(keepaliveIntervalSeconds));
            }

            if (maximumMessageBytes < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumMessageBytes));
            }

            this.streamEndpoint = streamEndpoint;
            this.keepaliveIntervalSeconds = keepaliveIntervalSeconds;
            this.maximumMessageBytes = maximumMessageBytes;
            this.parser = parser
                ?? throw new ArgumentNullException(nameof(parser));
            this.connectionFactory = connectionFactory
                ?? throw new ArgumentNullException(nameof(connectionFactory));
            this.keepaliveDelay = keepaliveDelay
                ?? throw new ArgumentNullException(nameof(keepaliveDelay));
        }

        public event Action<PhysiologyWindow> PhysiologyReceived;

        public event Action<SessionTransportStatus> StatusChanged;

        public event Action<ComponentBStressPayloadParseReasonCode>
            PayloadRejected;

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
                        "Component B can connect only while disconnected.");
                }

                PublishTransition(
                    SessionTransportConnectionState.Connecting,
                    SessionTransportStatusReason.ConnectRequested);

                IComponentBWebSocketConnection candidate = null;
                try
                {
                    candidate = connectionFactory.Create()
                        ?? throw new InvalidOperationException(
                            "The Component B connection factory returned null.");
                    await candidate.ConnectAsync(
                            streamEndpoint,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!candidate.IsOpen)
                    {
                        throw new InvalidOperationException(
                            "Component B connect completed without an open socket.");
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    candidate?.Dispose();
                    PublishTransition(
                        SessionTransportConnectionState.Disconnected,
                        SessionTransportStatusReason.OperationCancelled);
                    throw;
                }
                catch
                {
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
                        "Component B can disconnect only while connected.");
                }

                PublishTransition(
                    SessionTransportConnectionState.Disconnecting,
                    SessionTransportStatusReason.DisconnectRequested);

                IComponentBWebSocketConnection connection;
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
            IComponentBWebSocketConnection connection,
            CancellationTokenSource lifetime)
        {
            var diagnosticCode = RemoteClosedCode;
            var receiveTask = ReceiveLoopAsync(
                connection,
                lifetime.Token);
            var keepaliveTask = KeepaliveLoopAsync(
                connection,
                lifetime.Token);

            try
            {
                var firstCompleted = await Task.WhenAny(
                        receiveTask,
                        keepaliveTask)
                    .ConfigureAwait(false);
                diagnosticCode = receiveTask.IsCompleted
                    ? RemoteClosedCode
                    : KeepaliveFailureCode;
                await firstCompleted.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (lifetime.IsCancellationRequested)
            {
                diagnosticCode = null;
            }
            catch
            {
                if (diagnosticCode == RemoteClosedCode)
                {
                    diagnosticCode = ReceiveFailureCode;
                }
            }
            finally
            {
                lifetime.Cancel();
                connection.Abort();
                await ObserveTerminationAsync(receiveTask)
                    .ConfigureAwait(false);
                await ObserveTerminationAsync(keepaliveTask)
                    .ConfigureAwait(false);
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
            IComponentBWebSocketConnection connection,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested
                && connection.IsOpen)
            {
                var message = await connection.ReceiveTextAsync(
                        maximumMessageBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (message.CloseReceived)
                {
                    return;
                }

                if (parser.TryParse(
                        message.Text,
                        out var window,
                        out var rejectionReason))
                {
                    PhysiologyReceived?.Invoke(window);
                }
                else
                {
                    PayloadRejected?.Invoke(rejectionReason);
                }
            }
        }

        private async Task KeepaliveLoopAsync(
            IComponentBWebSocketConnection connection,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested
                && connection.IsOpen)
            {
                await keepaliveDelay.DelayAsync(
                        keepaliveIntervalSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await connection.SendTextAsync(
                        KeepaliveText,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static async Task ObserveTerminationAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        private void PublishTransition(
            SessionTransportConnectionState newState,
            SessionTransportStatusReason reason,
            string diagnosticCode = null)
        {
            SessionTransportStatus status;
            lock (synchronization)
            {
                status = new SessionTransportStatus(
                    connectionState,
                    newState,
                    reason,
                    diagnosticCode);
                connectionState = newState;
            }

            StatusChanged?.Invoke(status);
        }

        private static void ValidateEndpoint(Uri endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (!endpoint.IsAbsoluteUri
                || (!string.Equals(
                        endpoint.Scheme,
                        "ws",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        endpoint.Scheme,
                        "wss",
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "Component B requires an absolute ws:// or wss:// endpoint.",
                    nameof(endpoint));
            }
        }
    }
}
