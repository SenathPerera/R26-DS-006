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

    // Allowlisted local monitoring data. Never serialize identities or raw sensor buffers.
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
        public AgentMonitorSnapshot agents;
    }

    [Serializable]
    public sealed class AgentMonitorSnapshot
    {
        public string schemaVersion = "mindsync-agents-v1";
        public double capturedAt;
        public string phase;
        public AgentMonitorState visual;
        public AgentMonitorState audio;
        public AgentMonitorCoordination coordination;
    }

    [Serializable]
    public sealed class AgentMonitorCoordination
    {
        public string owner = "None";
        public string stage = "Waiting";
        public string reason = "Waiting for coordinated runtime";
        public double eligiblePostStartUtc;
        public double remainingSeconds;
        public long completedTurns;
        public long cancelledTurns;
    }

    [Serializable]
    public sealed class AgentMonitorState
    {
        public bool available;
        public string mode = "unknown";
        public string state = "unknown";
        public bool transitioning;
        public long updates;
        public float[] applied;
        public float[] target;
        public AgentMonitorInput input = new AgentMonitorInput();
        public AgentMonitorDecision decision = new AgentMonitorDecision();
        public AgentMonitorReward reward = new AgentMonitorReward();
    }

    [Serializable]
    public sealed class AgentMonitorInput
    {
        public bool available;
        public string source = "unknown";
        public double stress;
        public double confidence;
        public double quality;
        public double ageSeconds;
    }

    [Serializable]
    public sealed class AgentMonitorDecision
    {
        public bool available;
        public long sequence;
        public double at;
        public double stress;
        public string proposed = "unknown";
        public string executed = "unknown";
        public string safety = "unknown";
        public string result = "unknown";
        public float[] proposedDelta;
        public float[] safeDelta;
    }

    [Serializable]
    public sealed class AgentMonitorReward
    {
        public bool available;
        public bool valid;
        public double at;
        public long sequence;
        public string action = "unknown";
        public string status = "Waiting";
        public double total;
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
