using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public interface ISessionRelayConnectionTarget
    {
        SessionTransportConnectionState ConnectionState { get; }

        Task ConnectAsync(
            SessionRelayConnectionInfo connectionInfo,
            CancellationToken cancellationToken);

        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
