using System;
using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class ConnectionReconnectController
    {
        private readonly IReconnectableConnection connection;
        private readonly ReconnectBackoffConfiguration configuration;
        private readonly IReconnectDelay reconnectDelay;

        public ConnectionReconnectController(
            IReconnectableConnection connection,
            ReconnectBackoffConfiguration configuration,
            IReconnectDelay reconnectDelay)
        {
            this.connection = connection
                ?? throw new ArgumentNullException(nameof(connection));
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            this.reconnectDelay = reconnectDelay
                ?? throw new ArgumentNullException(nameof(reconnectDelay));
        }

        public async Task<ReconnectAttemptResult> ReconnectAsync(
            CancellationToken cancellationToken)
        {
            if (connection.ConnectionState
                == SessionTransportConnectionState.Connected)
            {
                return new ReconnectAttemptResult(true, 0, null);
            }

            if (connection.ConnectionState
                != SessionTransportConnectionState.Disconnected)
            {
                throw new InvalidOperationException(
                    "Reconnect can begin only while the connection is disconnected.");
            }

            Exception lastFailure = null;
            for (var attempt = 1;
                attempt <= configuration.MaximumAttempts;
                attempt++)
            {
                var delaySeconds = configuration.GetDelaySeconds(attempt);
                await reconnectDelay.DelayAsync(
                        delaySeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    await connection.ConnectAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (connection.ConnectionState
                        != SessionTransportConnectionState.Connected)
                    {
                        throw new InvalidOperationException(
                            "Connection completed without reaching Connected.");
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
                    if (connection.ConnectionState
                        != SessionTransportConnectionState.Disconnected)
                    {
                        throw new InvalidOperationException(
                            "Failed connection left an unrecoverable state.",
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
