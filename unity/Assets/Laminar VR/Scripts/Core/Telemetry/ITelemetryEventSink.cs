using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Telemetry
{
    public interface ITelemetryEventSink
    {
        Task AppendAsync(
            TelemetryEvent telemetryEvent,
            CancellationToken cancellationToken);

        Task FlushAsync(CancellationToken cancellationToken);
    }
}
