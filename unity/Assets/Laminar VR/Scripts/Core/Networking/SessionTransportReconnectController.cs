using System;
using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class SessionTransportReconnectController<
        TSessionConfiguration,
        TSessionCommand,
        TQuestState>
    {
        private readonly ISessionTransport<
            TSessionConfiguration,
            TSessionCommand,
            TQuestState> transport;
        private readonly ReconnectBackoffConfiguration configuration;
        private readonly IReconnectDelay reconnectDelay;

        public SessionTransportReconnectController(
            ISessionTransport<
                TSessionConfiguration,
                TSessionCommand,
                TQuestState> transport,
            ReconnectBackoffConfiguration configuration,
            IReconnectDelay reconnectDelay)
        {
            this.transport = transport
                ?? throw new ArgumentNullException(nameof(transport));
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            this.reconnectDelay = reconnectDelay
                ?? throw new ArgumentNullException(nameof(reconnectDelay));
        }

        public async Task<ReconnectAttemptResult> ReconnectAsync(
            CancellationToken cancellationToken)
        {
            if (transport.ConnectionState
                == SessionTransportConnectionState.Connected)
            {
                return new ReconnectAttemptResult(true, 0, null);
            }

            if (transport.ConnectionState
                != SessionTransportConnectionState.Disconnected)
            {
                throw new InvalidOperationException(
                    "Reconnect can begin only while the transport is disconnected.");
            }

            Exception lastFailure = null;
            for (var attempt = 1;
                attempt <= configuration.MaximumAttempts;
                attempt++)
            {
                var delaySeconds = configuration.GetDelaySeconds(attempt);
                await reconnectDelay.DelayAsync(delaySeconds, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    await transport.ConnectAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (transport.ConnectionState
                        != SessionTransportConnectionState.Connected)
                    {
                        throw new InvalidOperationException(
                            "Transport connect completed without reaching Connected.");
                    }

                    return new ReconnectAttemptResult(true, attempt, null);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                    if (transport.ConnectionState
                        != SessionTransportConnectionState.Disconnected)
                    {
                        throw new InvalidOperationException(
                            "Failed connection left transport in an unrecoverable state.",
                            exception);
                    }
                }
            }

            return new ReconnectAttemptResult(
                false,
                configuration.MaximumAttempts,
                lastFailure);
        }
    }
}
