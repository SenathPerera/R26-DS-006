using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public interface IReconnectDelay
    {
        Task DelayAsync(
            double delaySeconds,
            CancellationToken cancellationToken);
    }
}
