using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public interface IReconnectableConnection
    {
        SessionTransportConnectionState ConnectionState { get; }

        Task ConnectAsync(CancellationToken cancellationToken);
    }
}
