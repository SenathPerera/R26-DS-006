using System;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu(
        "Adaptive Meditation/Application/Session Relay Pairing Controller")]
    [DisallowMultipleComponent]
    public sealed class SessionRelayPairingController : MonoBehaviour
    {
        [Header("Relay Configuration")]
        [SerializeField]
        private SessionRelayConnectionProfile connectionProfile = null;

        [SerializeField]
        private SessionRelayBridge sessionRelayBridge = null;

        private readonly SemaphoreSlim operationGate =
            new SemaphoreSlim(1, 1);

        private ISessionRelayConnectionTarget connectionTargetOverride;
        private CancellationTokenSource componentLifetime;

        public bool IsPairing { get; private set; }

        public string LastPairingError { get; private set; } = string.Empty;

        public SessionTransportConnectionState ConnectionState =>
            ResolveConnectionTarget()?.ConnectionState
            ?? SessionTransportConnectionState.Disconnected;

        private void OnEnable()
        {
            componentLifetime = new CancellationTokenSource();
        }

        private void OnDisable()
        {
            componentLifetime?.Cancel();
            componentLifetime?.Dispose();
            componentLifetime = null;
        }

        public void Configure(
            SessionRelayConnectionProfile profile,
            ISessionRelayConnectionTarget connectionTarget)
        {
            if (IsPairing)
            {
                throw new InvalidOperationException(
                    "The pairing controller cannot be reconfigured while pairing.");
            }

            connectionProfile = profile;
            connectionTargetOverride = connectionTarget;
            if (connectionTarget is SessionRelayBridge bridge)
            {
                sessionRelayBridge = bridge;
            }
        }

        public bool TryValidateBindings(out string validationError)
        {
            if (connectionProfile == null)
            {
                validationError = "Assign a SessionRelayConnectionProfile.";
                return false;
            }

            if (ResolveConnectionTarget() == null)
            {
                validationError = "Assign a SessionRelayBridge.";
                return false;
            }

            validationError = string.Empty;
            return true;
        }

        public async Task PairAsync(
            string pairingCode,
            string questClientId,
            CancellationToken cancellationToken)
        {
            if (!TryValidateBindings(out var bindingError))
            {
                LastPairingError = bindingError;
                throw new InvalidOperationException(bindingError);
            }

            if (!connectionProfile.TryCreateConnectionInfo(
                    pairingCode,
                    questClientId,
                    UnityEngine.Application.version,
                    out var connectionInfo,
                    out var configurationError))
            {
                LastPairingError = configurationError;
                throw new InvalidOperationException(configurationError);
            }

            var lifetimeToken = componentLifetime?.Token
                ?? throw new InvalidOperationException(
                    "The pairing controller must be enabled before pairing.");

            await operationGate.WaitAsync(cancellationToken);
            try
            {
                var target = ResolveConnectionTarget();
                if (target.ConnectionState
                    != SessionTransportConnectionState.Disconnected)
                {
                    LastPairingError =
                        "The session relay is not disconnected.";
                    throw new InvalidOperationException(LastPairingError);
                }

                IsPairing = true;
                LastPairingError = string.Empty;
                using (var linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        lifetimeToken))
                {
                    await target.ConnectAsync(
                        connectionInfo,
                        linkedCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                LastPairingError = "relay-pairing-cancelled";
                throw;
            }
            catch (Exception exception)
            {
                if (string.IsNullOrEmpty(LastPairingError))
                {
                    LastPairingError =
                        "relay-pairing-failed:"
                        + exception.GetType().Name;
                }

                throw;
            }
            finally
            {
                IsPairing = false;
                operationGate.Release();
            }
        }

        public async Task DisconnectAsync(
            CancellationToken cancellationToken)
        {
            if (!TryValidateBindings(out var validationError))
            {
                throw new InvalidOperationException(validationError);
            }

            await operationGate.WaitAsync(cancellationToken);
            try
            {
                var target = ResolveConnectionTarget();
                if (target.ConnectionState
                    == SessionTransportConnectionState.Disconnected)
                {
                    return;
                }

                await target.DisconnectAsync(cancellationToken);
            }
            finally
            {
                operationGate.Release();
            }
        }

        private ISessionRelayConnectionTarget ResolveConnectionTarget()
        {
            return connectionTargetOverride ?? sessionRelayBridge;
        }
    }
}
