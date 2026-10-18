using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace AdaptiveAudioVR.Integration
{
    public class LyriaRealtimeStreamingService : MonoBehaviour
    {
        [Header("Runtime References")]
        [SerializeField] private PrototypeBootstrap bootstrap = null;
        [SerializeField] private AudioMixerController audioMixerController = null;
        [SerializeField] private LyriaClipGenerationService clipGenerationService = null;
        [SerializeField] private RealtimePcmAudioPlayer pcmAudioPlayer = null;

        [Header("Backend Connection")]
        [SerializeField] private string websocketBaseUrl = string.Empty;
        [SerializeField] private string realtimeModel = "models/lyria-realtime-exp";
        [SerializeField] private bool autoStartOnPlay = false;
        [SerializeField] private bool autoCheckCapabilityOnStart = true;

        [Header("Steering")]
        [SerializeField] private bool autoSteerFromRl = true;
        [SerializeField] private float steeringUpdateIntervalSeconds = 2.0f;
        [SerializeField] private float stressDeltaThreshold = 0.08f;
        [SerializeField] private float confidenceDeltaThreshold = 0.08f;
        [SerializeField] private float aggregateParameterDeltaThreshold = 0.12f;

        [Header("Playback")]
        [SerializeField] private float minimumBufferedAudioSeconds = 0.35f;
        [SerializeField] private float restoreCrossfadeSeconds = 1.25f;
        [SerializeField, Min(0f)] private float generationPlaybackGuardSeconds = 2f;
        [SerializeField] private bool restorePreviousClipOnStop = true;

        public bool IsConnecting { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsStreaming { get; private set; }
        public bool IsPaused { get; private set; }
        public bool HasBufferedPlaybackStarted { get; private set; }
        public string LastStatusMessage { get; private set; } = "Realtime idle.";
        public string LastWarningMessage { get; private set; } = string.Empty;
        public string LastErrorMessage { get; private set; } = string.Empty;
        public string LastServerState { get; private set; } = "idle";
        public bool HasCapabilityCheckResult { get; private set; }
        public bool IsCapabilityCheckRunning { get; private set; }
        public bool LastCapabilityAvailable { get; private set; }
        public string LastCapabilityMessage { get; private set; } = "Realtime capability not checked yet.";
        public string LastCapabilityCheckedAtUtc { get; private set; } = "never";
        public string LastCapabilityModel { get; private set; } = "unknown";
        public string LastSentPromptSignature { get; private set; } = string.Empty;
        public string LastPromptSummary { get; private set; } = string.Empty;
        public float BufferedSeconds => pcmAudioPlayer != null ? pcmAudioPlayer.BufferedSeconds : 0f;
        public long DroppedSampleCount => pcmAudioPlayer != null ? pcmAudioPlayer.DroppedSampleCount : 0;
        public long UnderflowSampleCount => pcmAudioPlayer != null ? pcmAudioPlayer.UnderflowSampleCount : 0;
        public bool IsRealtimeActive => IsConnected || IsConnecting;

        private readonly object inboundLock = new object();
        private readonly Queue<RealtimeInboundMessage> inboundMessages = new Queue<RealtimeInboundMessage>();

        private ClientWebSocket websocket;
        private CancellationTokenSource socketCancellation;
        private Task receiveLoopTask;
        private AudioClip fallbackMeditationClip;
        private bool clipGenerationWasEnabled;
        private float nextSteeringTime;
        private string lastObservedPromptSignature = string.Empty;
        private SignalPacket lastSentSignal;
        private AudioParameters lastSentParameters;
        private bool pendingBufferedPlaybackStart;
        private long contentReservation;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private bool pendingRealtimeStart;
        private bool pendingManualSync;
        private bool pendingManualAutoPlay;
        private RealtimeClientMessage activeGenerationMessage;
        private AudioParameters syncedParameters;
        private double syncedPlaybackReadyAt = double.PositiveInfinity;
        private AudioParameters fallbackParameters;
        private AudioParameters generationTarget;
        private string submittedGenerationId;
        private string acceptedGenerationId;
        private double generationReadyAt = double.PositiveInfinity;
        private bool generationFaulted;
        private bool backendTracksActionPlayback;
        private bool orphanedGeneration;
        private long observedUnderflows;
        private long observedDrops;
        private double playbackRecoveryUntil = -1;
        private readonly HashSet<string> issuedGenerationRequests = new HashSet<string>();
        private readonly HashSet<string> completedGenerationRequests = new HashSet<string>();

        private void Awake()
        {
            ResolveReferences();
            lastSentSignal = SignalPacket.CreateDefault();
        }

        private void Start()
        {
            if (autoCheckCapabilityOnStart)
            {
                RefreshRealtimeCapability();
            }

            if (autoStartOnPlay)
            {
                StartRealtimeStream();
            }
        }

        private void Update()
        {
            ProcessInboundMessages();
            TryStartBufferedPlayback();
            UpdateGenerationPlayback();

            if (IsConnected && !IsConnecting && Time.unscaledTime >= nextSteeringTime)
            {
                if (autoSteerFromRl && bootstrap != null && bootstrap.IsSessionRunning
                    && bootstrap.MonitorAgent != null && bootstrap.MonitorAgent.TryGetGenerationAction(out _, out _))
                    _ = TrySendCoordinatedGeneration();
                else if (pendingRealtimeStart) _ = StartRealtimeStreamAsync();
                else if (pendingManualSync) _ = SendCurrentFrameSyncAsync(pendingManualAutoPlay);
                else if (autoSteerFromRl) _ = TrySendRealtimeSteeringUpdate();
                nextSteeringTime = Time.unscaledTime + Mathf.Max(0.25f, steeringUpdateIntervalSeconds);
            }
        }

        private async void OnDestroy()
        {
            await DisconnectAsync(restoreClip: restorePreviousClipOnStop);
        }

        public void StartRealtimeStream()
        {
            if (IsCapabilityCheckRunning)
            {
                LastStatusMessage = "Realtime capability check is still running.";
                return;
            }

            if (!HasCapabilityCheckResult)
            {
                LastStatusMessage = "Checking realtime capability before starting. Press Start again once it reports ready.";
                RefreshRealtimeCapability();
                return;
            }

            if (!LastCapabilityAvailable)
            {
                LastStatusMessage = $"Realtime start blocked: {LastCapabilityMessage}";
                return;
            }

            pendingRealtimeStart = true;
            _ = StartRealtimeStreamAsync();
        }

        public void PauseRealtimeStream()
        {
            _ = SendPlaybackControlAsync("pause");
        }

        public void ResumeRealtimeStream()
        {
            _ = SendPlaybackControlAsync("play");
        }

        public void StopRealtimeStream()
        {
            _ = StopRealtimeStreamAsync();
        }

        public void StopForParticipantChange()
        {
            if (IsRealtimeActive) _ = DisconnectAsync(restoreClip: false);
        }

        public void PushCurrentFrameToRealtime()
        {
            pendingManualSync = true;
            pendingManualAutoPlay = IsStreaming || !IsPaused;
            _ = SendCurrentFrameSyncAsync(pendingManualAutoPlay);
        }

        public void RefreshRealtimeCapability()
        {
            if (IsCapabilityCheckRunning)
            {
                return;
            }

            StartCoroutine(CheckRealtimeCapabilityCoroutine());
        }

        private async Task StartRealtimeStreamAsync()
        {
            ResolveReferences();
            if (bootstrap == null || audioMixerController == null || pcmAudioPlayer == null)
            {
                LastErrorMessage = "Realtime stream cannot start because required references are missing.";
                LastStatusMessage = LastErrorMessage;
                return;
            }

            if (IsConnecting)
            {
                return;
            }

            if (IsConnected && IsPaused)
            {
                await SendPlaybackControlAsync("play");
                return;
            }

            if (!TryBuildSyncMessage(autoPlay: true, out RealtimeClientMessage syncMessage, out string promptSignature))
            {
                LastStatusMessage = "Realtime stream cannot start because the current Lyria frame is unavailable.";
                return;
            }
            AudioParameters syncParameters = bootstrap.CurrentParameters;

            if (!IsConnected)
            {
                IsConnecting = true;
                LastStatusMessage = "Connecting to local Lyria realtime bridge...";
                LastErrorMessage = string.Empty;

                fallbackMeditationClip = audioMixerController.CurrentMeditationClip;
                fallbackParameters = audioMixerController.CurrentAppliedParameters;
                generationFaulted = false;
                backendTracksActionPlayback = false;
                orphanedGeneration = false;
                playbackRecoveryUntil = -1;
                submittedGenerationId = acceptedGenerationId = null;
                issuedGenerationRequests.Clear();
                completedGenerationRequests.Clear();
                SuppressClipGeneration(true);
                pcmAudioPlayer.ClearBuffer();
                pcmAudioPlayer.ConfigureFormat(48000, 2);
                HasBufferedPlaybackStarted = false;
                pendingBufferedPlaybackStart = false;

                websocket = new ClientWebSocket();
                socketCancellation = new CancellationTokenSource();

                try
                {
                    await websocket.ConnectAsync(BuildRealtimeUri(), socketCancellation.Token);
                    IsConnected = true;
                    IsConnecting = false;
                    LastStatusMessage = "Connected to local realtime bridge.";
                    receiveLoopTask = ReceiveLoopAsync(websocket, socketCancellation.Token);
                }
                catch (Exception ex)
                {
                    LastErrorMessage = ex.Message;
                    LastStatusMessage = $"Realtime connect failed: {ex.Message}";
                    SetCapabilityResult(false, $"Local realtime bridge connect failed: {ex.Message}", realtimeModel);
                    IsConnecting = false;
                    IsConnected = false;
                    pendingRealtimeStart = false;
                    SuppressClipGeneration(false);
                    return;
                }
            }

            if (!await SendClientMessageAsync(syncMessage)) { pendingRealtimeStart = true; return; }
            activeGenerationMessage = syncMessage;
            syncedParameters = syncParameters;
            pendingRealtimeStart = false;
            LastSentPromptSignature = promptSignature;
            lastObservedPromptSignature = promptSignature;
            lastSentSignal = bootstrap.CurrentSignal;
            lastSentParameters = bootstrap.CurrentParameters;
            LastPromptSummary = bootstrap.CurrentPromptSummary;
            LastStatusMessage = "Realtime prompts/config synced.";
            IsPaused = false;
            IsStreaming = true;
            pendingBufferedPlaybackStart = true;
        }

        private async Task StopRealtimeStreamAsync()
        {
            await SendPlaybackControlAsync("stop");
            await DisconnectAsync(restoreClip: restorePreviousClipOnStop);
        }

        private async Task DisconnectAsync(bool restoreClip)
        {
            pendingRealtimeStart = pendingManualSync = false;
            pendingBufferedPlaybackStart = false;
            HasBufferedPlaybackStarted = false;
            IsStreaming = false;
            IsPaused = false;
            LastServerState = "closed";

            if (websocket != null)
            {
                try
                {
                    if (websocket.State == WebSocketState.Open)
                    {
                        await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                    }
                }
                catch
                {
                    // Ignore shutdown socket errors.
                }
                finally
                {
                    websocket.Dispose();
                    websocket = null;
                }
            }

            if (socketCancellation != null)
            {
                try
                {
                    socketCancellation.Cancel();
                }
                catch
                {
                    // Ignore cancellation disposal errors.
                }
                finally
                {
                    socketCancellation.Dispose();
                    socketCancellation = null;
                }
            }

            if (receiveLoopTask != null)
            {
                try
                {
                    await receiveLoopTask;
                }
                catch
                {
                    // Ignore receive loop completion errors during shutdown.
                }
                finally
                {
                    receiveLoopTask = null;
                }
            }

            IsConnected = false;
            IsConnecting = false;
            bootstrap?.MonitorAgent?.SetGenerationPlaybackAvailable(false);
            activeGenerationMessage = null;
            backendTracksActionPlayback = false;
            submittedGenerationId = acceptedGenerationId = null;
            syncedPlaybackReadyAt = double.PositiveInfinity;

            pcmAudioPlayer?.ClearBuffer();
            pcmAudioPlayer?.DetachFromMixer();
            bootstrap?.InterruptAdaptation("Realtime stream disconnected");

            if (restoreClip && restorePreviousClipOnStop && fallbackMeditationClip != null && audioMixerController != null)
            {
                audioMixerController.SetMeditationPlaybackPaused(false);
                audioMixerController.CrossfadeToMeditationClip(fallbackMeditationClip, restoreCrossfadeSeconds);
                audioMixerController.SetGeneratedControls(fallbackParameters);
                bootstrap?.MonitorAgent?.ReconcileGeneratedPlayback(fallbackParameters);
            }

            SuppressClipGeneration(false);
            bootstrap?.SetRealtimePlaybackReady(true);
            LastStatusMessage = restoreClip ? "Realtime stream stopped." : "Realtime stream disconnected.";
        }

        private async Task SendPlaybackControlAsync(string controlType)
        {
            if (!IsConnected)
            {
                LastStatusMessage = "Realtime stream is not connected.";
                return;
            }

            if (controlType == "pause" || controlType == "stop")
            {
                bootstrap?.InterruptAdaptation("Realtime playback interrupted");
                bootstrap?.SetRealtimePlaybackReady(false);
            }
            else if (controlType == "play" && IsPaused)
            {
                bootstrap?.InterruptAdaptation("Realtime music resuming");
                bootstrap?.SetRealtimePlaybackReady(true);
            }
            bool sent = await SendClientMessageAsync(new RealtimeClientMessage { type = controlType });
            if (controlType == "play") pendingRealtimeStart = !sent;
        }

        private async Task SendCurrentFrameSyncAsync(bool autoPlay)
        {
            if (!IsConnected)
            {
                if (!LastCapabilityAvailable)
                {
                    LastStatusMessage = $"Realtime sync blocked: {LastCapabilityMessage}";
                    return;
                }

                await StartRealtimeStreamAsync();
                return;
            }

            if (!TryBuildSyncMessage(autoPlay, out RealtimeClientMessage syncMessage, out string promptSignature))
            {
                return;
            }
            AudioParameters syncParameters = bootstrap.CurrentParameters;

            if (!await SendClientMessageAsync(syncMessage)) return;
            activeGenerationMessage = syncMessage;
            syncedParameters = syncParameters;
            generationFaulted = false;
            pendingManualSync = false;
            LastSentPromptSignature = promptSignature;
            lastObservedPromptSignature = promptSignature;
            lastSentSignal = bootstrap.CurrentSignal;
            lastSentParameters = bootstrap.CurrentParameters;
            LastPromptSummary = bootstrap.CurrentPromptSummary;
            if (autoPlay)
            {
                IsStreaming = true;
                IsPaused = false;
                pendingBufferedPlaybackStart = true;
            }
        }

        private async Task TrySendRealtimeSteeringUpdate()
        {
            // During a participant session only the reserved audio action may
            // steer generation. Raw stress changes cannot create a second action.
            if (bootstrap != null && bootstrap.IsSessionRunning && bootstrap.MonitorAgent != null)
            {
                await TrySendCoordinatedGeneration();
                return;
            }
            if (bootstrap == null || !TryBuildSyncMessage(autoPlay: false, out RealtimeClientMessage syncMessage, out string promptSignature))
            {
                return;
            }

            if (string.Equals(promptSignature, lastObservedPromptSignature, StringComparison.Ordinal))
            {
                return;
            }

            SignalPacket currentSignal = bootstrap.CurrentSignal;
            AudioParameters currentParameters = bootstrap.CurrentParameters;
            float stressDelta = Mathf.Abs(currentSignal.stress - lastSentSignal.stress);
            float confidenceDelta = Mathf.Abs(currentSignal.confidence - lastSentSignal.confidence);
            float parameterDelta =
                Mathf.Abs(currentParameters.intensity - lastSentParameters.intensity)
                + Mathf.Abs(currentParameters.density - lastSentParameters.density)
                + Mathf.Abs(currentParameters.brightness - lastSentParameters.brightness)
                + Mathf.Abs(currentParameters.ambientMix - lastSentParameters.ambientMix)
                + Mathf.Abs(currentParameters.musicMix - lastSentParameters.musicMix);

            bool shouldUpdate =
                stressDelta >= stressDeltaThreshold
                || confidenceDelta >= confidenceDeltaThreshold
                || parameterDelta >= aggregateParameterDeltaThreshold;

            if (!shouldUpdate)
            {
                return;
            }

            if (!await SendClientMessageAsync(syncMessage)) return;
            activeGenerationMessage = syncMessage;
            syncedParameters = currentParameters;
            LastSentPromptSignature = promptSignature;
            lastObservedPromptSignature = promptSignature;
            lastSentSignal = currentSignal;
            lastSentParameters = currentParameters;
            LastPromptSummary = bootstrap.CurrentPromptSummary;
            LastStatusMessage = "Realtime steering update sent.";
        }

        private async Task TrySendCoordinatedGeneration()
        {
            var agent = bootstrap?.MonitorAgent;
            if (agent == null || activeGenerationMessage == null
                || !agent.TryGetGenerationAction(out string requestId, out AudioParameters target)
                || requestId == submittedGenerationId) return;
            submittedGenerationId = requestId;
            acceptedGenerationId = null;
            orphanedGeneration = false;
            generationTarget = target;
            generationReadyAt = double.PositiveInfinity;
            issuedGenerationRequests.Add(requestId);
            pcmAudioPlayer.TrackPlaybackRequest(requestId, generationPlaybackGuardSeconds);
            var message = JsonUtility.FromJson<RealtimeClientMessage>(JsonUtility.ToJson(activeGenerationMessage));
            message.type = "config";
            message.requestId = requestId;
            message.autoPlay = false;
            message.config.density = target.density;
            message.config.bpm = AudioMusicalMapping.Bpm(target.tempo, agent.ActiveStrategy.bpmOffset);
            if (!await SendClientMessageAsync(message, requestId))
            {
                agent.FailGenerationAction(requestId, "Reserved Lyria steering failed");
                completedGenerationRequests.Add(requestId);
                generationFaulted = true;
                bootstrap.SetRealtimePlaybackReady(false);
                LastStatusMessage = "Reserved steering failed; stop and restart realtime music before learning.";
                return;
            }
            activeGenerationMessage = message;
            LastStatusMessage = "Reserved audio action sent; waiting for acknowledged PCM playback.";
        }

        private void UpdateGenerationPlayback()
        {
            var agent = bootstrap?.MonitorAgent;
            if (agent == null) return;
            if (Time.realtimeSinceStartupAsDouble >= syncedPlaybackReadyAt)
            {
                syncedPlaybackReadyAt = double.PositiveInfinity;
                audioMixerController.SetGeneratedControls(syncedParameters);
                agent.ReconcileGeneratedPlayback(syncedParameters);
            }
            bool playing = HasBufferedPlaybackStarted && IsStreaming && !IsPaused
                && audioMixerController != null && audioMixerController.IsMeditationPlaying;
            if (playing && (UnderflowSampleCount > observedUnderflows || DroppedSampleCount > observedDrops))
            {
                bootstrap.InterruptAdaptation("Realtime playback gap or dropped PCM; outcome discarded");
                bootstrap.SetRealtimePlaybackReady(false);
                playbackRecoveryUntil = Time.realtimeSinceStartupAsDouble + 5;
            }
            observedUnderflows = UnderflowSampleCount;
            observedDrops = DroppedSampleCount;
            agent.SetGenerationPlaybackAvailable(autoSteerFromRl && IsConnected && IsStreaming && !IsPaused
                && HasBufferedPlaybackStarted && backendTracksActionPlayback && activeGenerationMessage != null && !generationFaulted);
            bool outstanding = !string.IsNullOrEmpty(submittedGenerationId)
                && !completedGenerationRequests.Contains(submittedGenerationId);
            if (outstanding && !agent.IsPendingGenerationAction(submittedGenerationId) && !orphanedGeneration)
            {
                orphanedGeneration = true;
                bootstrap.InterruptAdaptation("Waiting for cancelled Lyria action to finish playback without reward");
                bootstrap.SetRealtimePlaybackReady(false);
            }
            if (playbackRecoveryUntil >= 0 && Time.realtimeSinceStartupAsDouble >= playbackRecoveryUntil
                && playing && BufferedSeconds >= minimumBufferedAudioSeconds && !generationFaulted && !orphanedGeneration)
            {
                playbackRecoveryUntil = -1;
                bootstrap.InterruptAdaptation("Realtime playback recovered; waiting for clean physiology");
                bootstrap.SetRealtimePlaybackReady(true);
            }
            if (!outstanding || acceptedGenerationId != submittedGenerationId || generationFaulted) return;
            bool rendered = pcmAudioPlayer.HasRenderedRequest(submittedGenerationId, out bool lost);
            if (lost)
            {
                generationFaulted = true;
                bootstrap.InterruptAdaptation("Action PCM marker lost; stop and restart realtime music");
                bootstrap.SetRealtimePlaybackReady(false);
                return;
            }
            if (!rendered || audioMixerController == null || !audioMixerController.IsMeditationPlaying
                || audioMixerController.IsMeditationPlaybackPaused || audioMixerController.IsMuted) return;
            if (double.IsPositiveInfinity(generationReadyAt))
            {
                AudioSettings.GetDSPBufferSize(out int length, out int count);
                // A streaming clip callback may fill ahead of the speaker cursor.
                generationReadyAt = Time.realtimeSinceStartupAsDouble
                    + (pcmAudioPlayer.StreamingClip != null ? pcmAudioPlayer.StreamingClip.length : 0)
                    + Math.Max(0.1, (double)length * count / Math.Max(1, AudioSettings.outputSampleRate));
            }
            if (Time.realtimeSinceStartupAsDouble < generationReadyAt) return;
            bool credited = agent.ConfirmGenerationPlayback(submittedGenerationId);
            audioMixerController.SetGeneratedControls(generationTarget);
            syncedParameters = generationTarget;
            completedGenerationRequests.Add(submittedGenerationId);
            if (!credited)
            {
                agent.ReconcileGeneratedPlayback(generationTarget);
                bootstrap.InterruptAdaptation("Cancelled Lyria action reached playback; no reward assigned");
                bootstrap.SetRealtimePlaybackReady(playbackRecoveryUntil < 0);
            }
            orphanedGeneration = false;
            LastStatusMessage = credited ? "Reserved Lyria action reached playback; agent settling can begin."
                : "Cancelled generation reconciled at playback without learning.";
        }

        private bool TryBuildSyncMessage(bool autoPlay, out RealtimeClientMessage message, out string promptSignature)
        {
            message = null;
            promptSignature = string.Empty;

            ResolveReferences();
            if (bootstrap == null || !bootstrap.IsPrepared || bootstrap.CurrentLyriaFrame == null)
            {
                return false;
            }

            LyriaControlFrame frame = bootstrap.CurrentLyriaFrame;
            frame.Normalize();

            RealtimeWeightedPrompt[] prompts = BuildWeightedPromptDtos(frame);
            RealtimeMusicConfig config = RealtimeMusicConfig.From(frame.config);
            promptSignature = BuildPromptSignature(frame, bootstrap.CurrentSignal, bootstrap.CurrentParameters);

            message = new RealtimeClientMessage
            {
                type = "sync",
                autoPlay = autoPlay,
                weightedPrompts = prompts,
                config = config,
            };
            return true;
        }

        private static RealtimeWeightedPrompt[] BuildWeightedPromptDtos(LyriaControlFrame frame)
        {
            if (frame.weightedPrompts == null || frame.weightedPrompts.Length == 0)
            {
                return new[] { new RealtimeWeightedPrompt { text = "calm ambient meditation", weight = 1f } };
            }

            int count = Mathf.Min(frame.weightedPrompts.Length, 8);
            var prompts = new List<RealtimeWeightedPrompt>(count);
            for (int i = 0; i < frame.weightedPrompts.Length && prompts.Count < count; i++)
            {
                PromptWeight prompt = frame.weightedPrompts[i].Normalize();
                if (string.IsNullOrWhiteSpace(prompt.text) || Mathf.Abs(prompt.weight) < 0.001f)
                {
                    continue;
                }

                prompts.Add(new RealtimeWeightedPrompt
                {
                    text = prompt.text,
                    weight = prompt.weight,
                });
            }

            return prompts.Count > 0
                ? prompts.ToArray()
                : new[] { new RealtimeWeightedPrompt { text = "calm ambient meditation", weight = 1f } };
        }

        private static string BuildPromptSignature(LyriaControlFrame frame, SignalPacket signal, AudioParameters parameters)
        {
            var builder = new StringBuilder();
            builder.Append(frame.strategyName);
            builder.Append('|');
            builder.Append(frame.actionName);
            builder.Append('|');
            builder.Append(signal.stress.ToString("F2"));
            builder.Append('|');
            builder.Append(signal.confidence.ToString("F2"));
            builder.Append('|');
            builder.Append(parameters.intensity.ToString("F2"));
            builder.Append('|');
            builder.Append(parameters.density.ToString("F2"));
            builder.Append('|');
            builder.Append(parameters.brightness.ToString("F2"));
            builder.Append('|');
            builder.Append(parameters.musicMix.ToString("F2"));
            builder.Append('|');
            builder.Append(parameters.ambientMix.ToString("F2"));
            builder.Append('|');
            builder.Append(frame.config.bpm);
            builder.Append('|');
            builder.Append(frame.config.scale);
            return builder.ToString();
        }

        private async Task<bool> SendClientMessageAsync(RealtimeClientMessage message, string ownedAudioRequestId = null)
        {
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                LastStatusMessage = "Realtime websocket is not open.";
                return false;
            }

            await sendLock.WaitAsync();
            try
            {
                if (ownedAudioRequestId != null && !(bootstrap?.MonitorAgent?.IsPendingGenerationAction(ownedAudioRequestId) ?? false))
                    return false;
                bool changesContent = message.type == "sync" || message.type == "prompts"
                    || message.type == "config" || message.type == "play";
                if (ownedAudioRequestId == null && changesContent && bootstrap != null && bootstrap.IsSessionRunning && !(message.type == "play" && IsPaused))
                {
                    if (contentReservation != 0 && bootstrap.SharedAdaptation != null
                        && bootstrap.SharedAdaptation.IsCurrent(contentReservation)) return false;
                    if (!bootstrap.TryBeginContentChange("Realtime music steering", out contentReservation))
                    {
                        LastStatusMessage = "Realtime steering deferred during coordinated measurement.";
                        return false;
                    }
                }
                string json = JsonUtility.ToJson(message);
                byte[] payload = Encoding.UTF8.GetBytes(json);
                await websocket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true,
                    socketCancellation != null ? socketCancellation.Token : CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                LastErrorMessage = ex.Message;
                bootstrap?.InterruptAdaptation("Realtime command failed");
                bootstrap?.SetRealtimePlaybackReady(false);
                return false;
            }
            finally { sendLock.Release(); }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    string message = await ReceiveTextMessageAsync(socket, cancellationToken);
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }

                    RealtimeInboundMessage inbound = JsonUtility.FromJson<RealtimeInboundMessage>(message);
                    if (inbound == null)
                    {
                        continue;
                    }

                    if (string.Equals(inbound.type, "audio", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(inbound.data))
                        {
                            try
                            {
                                byte[] pcmBytes = Convert.FromBase64String(inbound.data);
                                if (inbound.sampleRate != 48000 || inbound.channels != 2 || inbound.format != "pcm_s16le")
                                {
                                    throw new InvalidOperationException("Unsupported realtime PCM format.");
                                }

                                pcmAudioPlayer.EnqueuePcm16(pcmBytes, inbound.requestId);
                            }
                            catch (Exception ex)
                            {
                                EnqueueInbound(new RealtimeInboundMessage
                                {
                                    type = "error",
                                    message = $"Failed to decode realtime audio chunk: {ex.Message}",
                                });
                            }
                        }

                        continue;
                    }

                    EnqueueInbound(inbound);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
            catch (Exception ex)
            {
                EnqueueInbound(new RealtimeInboundMessage
                {
                    type = "error",
                    message = $"Realtime receive loop failed: {ex.Message}",
                });
            }
            finally
            {
                EnqueueInbound(new RealtimeInboundMessage
                {
                    type = "state",
                    state = "closed",
                    message = "Realtime websocket closed.",
                });
            }
        }

        private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[8192]);
            using (var stream = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return string.Empty;
                    }

                    stream.Write(buffer.Array, buffer.Offset, result.Count);
                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private void EnqueueInbound(RealtimeInboundMessage inbound)
        {
            lock (inboundLock)
            {
                inboundMessages.Enqueue(inbound);
            }
        }

        private void ProcessInboundMessages()
        {
            while (true)
            {
                RealtimeInboundMessage inbound;
                lock (inboundLock)
                {
                    if (inboundMessages.Count == 0)
                    {
                        break;
                    }

                    inbound = inboundMessages.Dequeue();
                }

                HandleInboundMessage(inbound);
            }
        }

        private void HandleInboundMessage(RealtimeInboundMessage inbound)
        {
            if (inbound == null)
            {
                return;
            }

            switch ((inbound.type ?? string.Empty).ToLowerInvariant())
            {
                case "connected":
                    backendTracksActionPlayback = inbound.actionPlaybackProtocol == AudioPlaybackMarker.Protocol;
                    if (!backendTracksActionPlayback)
                        LastWarningMessage = "Bridge lacks action playback tracking; density/tempo actions disabled. Restart the updated backend.";
                    LastServerState = "connected";
                    LastStatusMessage = string.IsNullOrWhiteSpace(inbound.message) ? "Realtime bridge connected." : inbound.message;
                    SetCapabilityResult(true, "Realtime session opened successfully.", string.IsNullOrWhiteSpace(inbound.model) ? realtimeModel : inbound.model);
                    break;
                case "state":
                    LastServerState = string.IsNullOrWhiteSpace(inbound.state) ? "state" : inbound.state;
                    LastStatusMessage = string.IsNullOrWhiteSpace(inbound.message) ? LastStatusMessage : inbound.message;
                    if (!string.IsNullOrEmpty(inbound.requestId) && issuedGenerationRequests.Contains(inbound.requestId)
                        && (inbound.state == "synced" || inbound.state == "config_updated"))
                    {
                        if (completedGenerationRequests.Contains(inbound.requestId)) break;
                        if (inbound.requestId == submittedGenerationId)
                            acceptedGenerationId = inbound.requestId;
                        else
                        {
                            generationFaulted = true;
                            bootstrap?.InterruptAdaptation("Late Lyria action acknowledgement after cancellation");
                            bootstrap?.SetRealtimePlaybackReady(false);
                        }
                        break;
                    }
                    if (string.Equals(inbound.state, "playing", StringComparison.OrdinalIgnoreCase))
                    {
                        bootstrap?.SetRealtimePlaybackReady(true);
                        if (HasBufferedPlaybackStarted)
                        {
                            CompleteSyncedContentAtPlayback();
                        }
                        IsStreaming = true;
                        IsPaused = false;
                        pendingBufferedPlaybackStart = true;
                        if (audioMixerController != null)
                        {
                            audioMixerController.SetMeditationPlaybackPaused(false);
                        }
                    }
                    else if (string.Equals(inbound.state, "paused", StringComparison.OrdinalIgnoreCase))
                    {
                        bootstrap?.InterruptAdaptation("Realtime music paused");
                        bootstrap?.SetRealtimePlaybackReady(false);
                        IsPaused = true;
                        IsStreaming = false;
                        if (audioMixerController != null)
                        {
                            audioMixerController.SetMeditationPlaybackPaused(true);
                        }
                    }
                    else if (string.Equals(inbound.state, "stopped", StringComparison.OrdinalIgnoreCase))
                    {
                        bootstrap?.InterruptAdaptation("Realtime music stopped");
                        bootstrap?.SetRealtimePlaybackReady(false);
                        IsPaused = false;
                        IsStreaming = false;
                        pendingBufferedPlaybackStart = false;
                        pcmAudioPlayer.ClearBuffer();
                        if (audioMixerController != null)
                        {
                            audioMixerController.SetMeditationPlaybackPaused(false);
                        }
                    }
                    else if (string.Equals(inbound.state, "closed", StringComparison.OrdinalIgnoreCase))
                    {
                        bootstrap?.InterruptAdaptation("Realtime music connection closed");
                        bootstrap?.SetRealtimePlaybackReady(false);
                        IsConnected = false;
                        IsConnecting = false;
                        IsStreaming = false;
                        IsPaused = false;
                        pendingBufferedPlaybackStart = false;
                        HasBufferedPlaybackStarted = false;
                        SuppressClipGeneration(false);
                    }
                    else if (inbound.state == "synced" || inbound.state == "config_updated" || inbound.state == "prompts_updated")
                    {
                        if (contentReservation != 0 && bootstrap?.SharedAdaptation != null
                            && !bootstrap.SharedAdaptation.IsCurrent(contentReservation))
                        {
                            bootstrap.InterruptAdaptation("Realtime steering recovered after interruption");
                            bootstrap.SetRealtimePlaybackReady(true);
                        }
                        CompleteSyncedContentAtPlayback();
                    }
                    break;
                case "warning":
                    LastWarningMessage = inbound.message ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(inbound.message))
                    {
                        LastStatusMessage = inbound.message;
                    }
                    break;
                case "filtered_prompt":
                    generationFaulted = true;
                    bootstrap?.InterruptAdaptation("Lyria rejected the requested music prompt");
                    bootstrap?.SetRealtimePlaybackReady(false);
                    LastWarningMessage = $"Filtered prompt: {inbound.filteredReason}";
                    LastStatusMessage = string.IsNullOrWhiteSpace(inbound.message) ? LastWarningMessage : inbound.message;
                    break;
                case "error":
                    generationFaulted = true;
                    bootstrap?.InterruptAdaptation("Realtime music error");
                    bootstrap?.SetRealtimePlaybackReady(false);
                    LastErrorMessage = inbound.message ?? "Unknown realtime error.";
                    LastStatusMessage = LastErrorMessage;
                    if (!string.IsNullOrWhiteSpace(LastErrorMessage))
                    {
                        SetCapabilityResult(false, LastErrorMessage, realtimeModel);
                    }
                    break;
            }
        }

        private void TryStartBufferedPlayback()
        {
            if (!pendingBufferedPlaybackStart || HasBufferedPlaybackStarted || pcmAudioPlayer == null || audioMixerController == null)
            {
                return;
            }

            if (BufferedSeconds < Mathf.Max(0.05f, minimumBufferedAudioSeconds))
            {
                return;
            }

            if (bootstrap != null && bootstrap.IsSessionRunning
                && (contentReservation == 0 || bootstrap.SharedAdaptation == null
                    || !bootstrap.SharedAdaptation.IsCurrent(contentReservation))
                && !bootstrap.TryBeginContentChange("Realtime playback attachment", out contentReservation)) return;

            pcmAudioPlayer.AttachToMixer(audioMixerController, restartPlayback: true);
            audioMixerController.SetGeneratedControls(syncedParameters);
            CompleteSyncedContentAtPlayback();
            audioMixerController.SetMeditationPlaybackPaused(false);
            HasBufferedPlaybackStarted = true;
            pendingBufferedPlaybackStart = false;
            LastStatusMessage = $"Realtime audio playback started with {BufferedSeconds:F2}s buffer.";
        }

        private void CompleteSyncedContentAtPlayback()
        {
            float delay = BufferedSeconds + generationPlaybackGuardSeconds
                + (pcmAudioPlayer.StreamingClip != null ? pcmAudioPlayer.StreamingClip.length : 0);
            syncedPlaybackReadyAt = Time.realtimeSinceStartupAsDouble + delay;
            bootstrap?.CompleteContentChange(contentReservation, delay);
            contentReservation = 0;
        }

        private void SuppressClipGeneration(bool suppress)
        {
            ResolveReferences();
            if (clipGenerationService == null)
            {
                return;
            }

            if (suppress)
            {
                clipGenerationWasEnabled = clipGenerationService.enabled;
                clipGenerationService.enabled = false;
            }
            else
            {
                clipGenerationService.enabled = clipGenerationWasEnabled;
            }
        }

        private Uri BuildRealtimeUri()
        {
            string separator = websocketBaseUrl.Contains("?") ? "&" : "?";
            string modelQuery = Uri.EscapeDataString(realtimeModel);
            return new Uri($"{websocketBaseUrl}{separator}model={modelQuery}");
        }

        private string BuildCapabilityUrl()
        {
            if (!Uri.TryCreate(websocketBaseUrl, UriKind.Absolute, out Uri websocketUri))
            {
                return string.Empty;
            }

            string scheme = websocketUri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
            var builder = new UriBuilder(websocketUri)
            {
                Scheme = scheme,
                Path = "/realtime-capability",
                Query = $"model={UnityWebRequest.EscapeURL(realtimeModel)}",
            };
            return builder.Uri.AbsoluteUri;
        }

        private System.Collections.IEnumerator CheckRealtimeCapabilityCoroutine()
        {
            IsCapabilityCheckRunning = true;
            LastStatusMessage = "Checking realtime capability...";

            string capabilityUrl = BuildCapabilityUrl();
            if (string.IsNullOrWhiteSpace(capabilityUrl))
            {
                const string Error =
                    "Realtime capability check requires a configured backend endpoint.";
                SetCapabilityResult(false, Error, realtimeModel);
                LastStatusMessage = Error;
                IsCapabilityCheckRunning = false;
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequest.Get(capabilityUrl))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"Realtime capability request failed: {request.error}";
                    SetCapabilityResult(false, error, realtimeModel);
                    LastStatusMessage = error;
                    IsCapabilityCheckRunning = false;
                    yield break;
                }

                RealtimeCapabilityResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<RealtimeCapabilityResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    string parseError = $"Realtime capability response parse failed: {ex.Message}";
                    SetCapabilityResult(false, parseError, realtimeModel);
                    LastStatusMessage = parseError;
                    IsCapabilityCheckRunning = false;
                    yield break;
                }

                if (response == null)
                {
                    string emptyError = "Realtime capability response was empty.";
                    SetCapabilityResult(false, emptyError, realtimeModel);
                    LastStatusMessage = emptyError;
                    IsCapabilityCheckRunning = false;
                    yield break;
                }

                SetCapabilityResult(
                    response.available,
                    response.message,
                    string.IsNullOrWhiteSpace(response.model) ? realtimeModel : response.model,
                    response.checkedAtUtc);
                LastStatusMessage = response.available
                    ? "Realtime capability ready. You can start streaming."
                    : $"Realtime unavailable: {response.message}";
            }

            IsCapabilityCheckRunning = false;
        }

        private void SetCapabilityResult(bool available, string message, string model, string checkedAtUtc = null)
        {
            HasCapabilityCheckResult = true;
            LastCapabilityAvailable = available;
            LastCapabilityMessage = string.IsNullOrWhiteSpace(message) ? (available ? "Realtime ready." : "Realtime unavailable.") : message;
            LastCapabilityModel = string.IsNullOrWhiteSpace(model) ? realtimeModel : model;
            LastCapabilityCheckedAtUtc = string.IsNullOrWhiteSpace(checkedAtUtc) ? DateTime.UtcNow.ToString("o") : checkedAtUtc;
        }

        private void ResolveReferences()
        {
            bootstrap ??= FindAnyObjectByType<PrototypeBootstrap>();
            audioMixerController ??= FindAnyObjectByType<AudioMixerController>();
            clipGenerationService ??= FindAnyObjectByType<LyriaClipGenerationService>();
            pcmAudioPlayer ??= GetComponent<RealtimePcmAudioPlayer>();
            if (pcmAudioPlayer == null)
            {
                pcmAudioPlayer = gameObject.AddComponent<RealtimePcmAudioPlayer>();
            }
        }

        [Serializable]
        private class RealtimeClientMessage
        {
            public string type;
            public string requestId;
            public bool autoPlay;
            public RealtimeWeightedPrompt[] weightedPrompts;
            public RealtimeMusicConfig config;
        }

        [Serializable]
        private class RealtimeWeightedPrompt
        {
            public string text;
            public float weight;
        }

        [Serializable]
        private class RealtimeMusicConfig
        {
            public float temperature;
            public int topK;
            public int seed;
            public float guidance;
            public int bpm;
            public float density;
            public float brightness;
            public string scale;
            public bool muteBass;
            public bool muteDrums;
            public bool onlyBassAndDrums;
            public string musicGenerationMode;

            public static RealtimeMusicConfig From(LyriaGenerationConfig config)
            {
                config = config.Normalize();
                return new RealtimeMusicConfig
                {
                    temperature = config.temperature,
                    topK = config.topK,
                    seed = config.seed,
                    guidance = config.guidance,
                    bpm = config.bpm,
                    density = config.density,
                    brightness = config.brightness,
                    scale = config.scale.ToString(),
                    muteBass = config.muteBass,
                    muteDrums = config.muteDrums,
                    onlyBassAndDrums = config.onlyBassAndDrums,
                    musicGenerationMode = config.musicGenerationMode.ToString(),
                };
            }
        }

        [Serializable]
        private class RealtimeInboundMessage
        {
            public string type = string.Empty;
            public string state = string.Empty;
            public string message = string.Empty;
            public string model = string.Empty;
            public string data = string.Empty;
            public int sampleRate = 0;
            public int channels = 0;
            public string format = string.Empty;
            public string filteredReason = string.Empty;
            public string requestId = string.Empty;
            public string actionPlaybackProtocol = string.Empty;
        }

        [Serializable]
        private class RealtimeCapabilityResponse
        {
            public bool available = false;
            public string model = string.Empty;
            public string checkedAtUtc = string.Empty;
            public string message = string.Empty;
        }
    }
}
