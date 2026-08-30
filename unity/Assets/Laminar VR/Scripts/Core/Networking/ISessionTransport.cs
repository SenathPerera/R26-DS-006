using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    // TODO(RESEARCH_DECISION): Replace the remaining generic message types with
    // shared versioned contracts after the session configuration, command, and
    // Quest-state schemas are frozen. Concrete adapters own serialization and
    // must emit validated domain messages through this boundary.
    public interface ISessionTransport<
        TSessionConfiguration,
        TSessionCommand,
        TQuestState>
    {
        event Action<TSessionConfiguration> SessionConfigurationReceived;

        event Action<PhysiologyWindow> PhysiologyReceived;

        event Action<TSessionCommand> SessionCommandReceived;

        event Action<SessionTransportStatus> StatusChanged;

        SessionTransportConnectionState ConnectionState { get; }

        Task ConnectAsync(CancellationToken cancellationToken);

        Task PublishQuestStateAsync(
            TQuestState state,
            CancellationToken cancellationToken);

        Task PublishTelemetryBatchAsync(
            IReadOnlyList<TelemetryEvent> telemetryEvents,
            CancellationToken cancellationToken);

        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
