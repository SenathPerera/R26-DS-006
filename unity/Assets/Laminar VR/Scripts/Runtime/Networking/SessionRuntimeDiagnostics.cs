using System;
using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public interface ISessionRelayDiagnosticsPublisher
    {
        Task PublishDiagnosticsAsync(string sessionId, SessionRuntimeDiagnostics values,
            CancellationToken cancellationToken);
    }

    // Operational flags only. No participant IDs, prompts or physiological values.
    [Serializable]
    public sealed class SessionRuntimeDiagnostics
    {
        public string phase;
        public string visualPolicy;
        public bool visualInitialized;
        public string audioPolicy;
        public bool audioPrepared;
        public bool audioRunning;
        public bool audioPlaying;
        public bool audioMuted;
        public bool generatedClipReady;
        public bool generating;
        public bool audioBackendReachable;
        public bool audioBackendConfigured;
        public bool audioBackendSdkReady;
        public int audioGenerationFailures;
        public int audioOnlineUpdates;
        public int physiologyPayloads;
        public string physiologySocket;
        public int visualPhysiologyWindows;
        public float physiologyAgeSeconds;
        public bool audioPreferenceError;
        public int pendingTelemetry;
    }

    [Serializable]
    internal sealed class DiagnosticsEnvelope
    {
        public string schemaVersion = "mindsync-monitor-v1";
        public string messageType = "client_diagnostics";
        public DiagnosticsPayload payload;
    }

    [Serializable]
    internal sealed class DiagnosticsPayload
    {
        public string sessionId;
        public SessionRuntimeDiagnostics values;
    }
}
