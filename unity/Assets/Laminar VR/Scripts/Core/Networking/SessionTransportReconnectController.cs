using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class SessionTransportReconnectController<
        TSessionConfiguration,
        TSessionCommand,
        TQuestState>
    {
        private readonly ConnectionReconnectController reconnectController;

        public SessionTransportReconnectController(
            ISessionTransport<
                TSessionConfiguration,
                TSessionCommand,
                TQuestState> transport,
            ReconnectBackoffConfiguration configuration,
            IReconnectDelay reconnectDelay)
        {
            reconnectController = new ConnectionReconnectController(
                transport,
                configuration,
                reconnectDelay);
        }

        public async Task<ReconnectAttemptResult> ReconnectAsync(
            CancellationToken cancellationToken)
        {
            return await reconnectController
                .ReconnectAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
