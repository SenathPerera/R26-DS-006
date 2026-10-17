using System;
using System.Threading;
using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Core;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using UnityEngine;

namespace AdaptiveAudioVR.Integration
{
    [DisallowMultipleComponent]
    public sealed class ProjectMonitorReporter : MonoBehaviour
    {
        private PrototypeBootstrap audioBootstrap;
        private LyriaClipGenerationService generation;
        private ComponentBStressSignalReceiver physiology;
        private AudioSessionPreferenceReceiver preferences;
        private AudioMixerController mixer;
        private SessionRelayBridge relay;
        private ApplicationBootstrap visual;
        private ProductionSessionCoordinator coordinator;
        private ComponentBPhysiologyBridge physiologyBridge;
        private CancellationTokenSource lifetime;
        private bool sending;
        private float nextSend;
        private float lastPhysiologyAt = -1f;
        private int lastPhysiologyCount;

        public void Configure(PrototypeBootstrap bootstrap, LyriaClipGenerationService clips,
            ComponentBStressSignalReceiver receiver, AudioSessionPreferenceReceiver preferenceReceiver,
            AudioMixerController audioMixer)
        {
            audioBootstrap = bootstrap;
            generation = clips;
            physiology = receiver;
            preferences = preferenceReceiver;
            mixer = audioMixer;
        }

        private void OnEnable() => lifetime = new CancellationTokenSource();

        private void OnDisable()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
        }

        private void Update()
        {
            if (physiology != null && physiology.ReceivedPayloadCount != lastPhysiologyCount)
            {
                lastPhysiologyCount = physiology.ReceivedPayloadCount;
                lastPhysiologyAt = Time.unscaledTime;
            }
            if (sending || Time.unscaledTime < nextSend || audioBootstrap == null || generation == null) return;
            nextSend = Time.unscaledTime + 5f;
            if (relay == null) relay = FindAnyObjectByType<SessionRelayBridge>();
            if (visual == null) visual = FindAnyObjectByType<ApplicationBootstrap>();
            if (coordinator == null) coordinator = FindAnyObjectByType<ProductionSessionCoordinator>();
            if (physiologyBridge == null) physiologyBridge = FindAnyObjectByType<ComponentBPhysiologyBridge>();
            if (relay == null || relay.CurrentConfiguration == null) return;
            SendReport(lifetime.Token);
        }

        private async void SendReport(CancellationToken token)
        {
            sending = true;
            try
            {
                await relay.PublishDiagnosticsAsync(new SessionRuntimeDiagnostics
                {
                    phase = coordinator != null ? coordinator.Phase.ToString() : "unknown",
                    visualPolicy = visual != null ? visual.StudyPolicyMode.ToString() : "unknown",
                    visualInitialized = visual != null && visual.IsInitialized,
                    audioPolicy = audioBootstrap.CurrentRLMode.ToString(),
                    audioPrepared = audioBootstrap.IsPrepared,
                    audioRunning = audioBootstrap.IsSessionRunning,
                    audioPlaying = mixer != null && mixer.IsMeditationPlaying,
                    audioMuted = mixer != null && mixer.IsMuted,
                    generatedClipReady = generation.IsInitialGeneratedClipReady,
                    generating = generation.IsGenerating,
                    audioBackendReachable = generation.IsBackendReachable,
                    audioBackendConfigured = generation.IsBackendConfigured,
                    audioBackendSdkReady = generation.IsBackendSdkReady,
                    audioGenerationFailures = generation.FailedGenerationCount,
                    audioOnlineUpdates = audioBootstrap.CurrentOnlineUpdateCount,
                    physiologySocket = physiologyBridge != null ? physiologyBridge.ConnectionState.ToString() : "unknown",
                    visualPhysiologyWindows = physiologyBridge != null ? physiologyBridge.ForwardedWindowCount : 0,
                    physiologyPayloads = lastPhysiologyCount,
                    physiologyAgeSeconds = lastPhysiologyAt < 0 ? -1 : Time.unscaledTime - lastPhysiologyAt,
                    audioPreferenceError = preferences != null && !string.IsNullOrEmpty(preferences.LastError),
                    pendingTelemetry = relay.PendingTelemetryEventCount,
                }, token);
            }
            catch (Exception)
            {
                // Best effort. Monitoring failures must never interrupt a participant session.
            }
            finally { sending = false; }
        }
    }
}
