using System;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public interface IPhysiologyStreamSource : IReconnectableConnection
    {
        event Action<PhysiologyWindow> PhysiologyReceived;

        event Action<SessionTransportStatus> StatusChanged;

        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
