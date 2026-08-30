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
        [SerializeField] private string websocketBaseUrl = "ws://127.0.0.1:8000/live-music";
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

            if (autoSteerFromRl && IsConnected && !IsConnecting && Time.unscaledTime >= nextSteeringTime)
            {
                TrySendRealtimeSteeringUpdate();
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

        public void PushCurrentFrameToRealtime()
        {
            _ = SendCurrentFrameSyncAsync(autoPlay: IsStreaming || !IsPaused);
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

            if (!TryBuildSyncMessage(autoPlay: true, out RealtimeClientMessage syncMessage, out string promptSignature))
            {
                LastStatusMessage = "Realtime stream cannot start because the current Lyria frame is unavailable.";
                return;
            }

            if (!IsConnected)
            {
                IsConnecting = true;
                LastStatusMessage = "Connecting to local Lyria realtime bridge...";
                LastErrorMessage = string.Empty;

                fallbackMeditationClip = audioMixerController.CurrentMeditationClip;
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
                    SuppressClipGeneration(false);
                    return;
                }
            }

            await SendClientMessageAsync(syncMessage);
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

            pcmAudioPlayer?.ClearBuffer();
            pcmAudioPlayer?.DetachFromMixer();

            if (restoreClip && restorePreviousClipOnStop && fallbackMeditationClip != null && audioMixerController != null)
            {
                audioMixerController.SetMeditationPlaybackPaused(false);
                audioMixerController.CrossfadeToMeditationClip(fallbackMeditationClip, restoreCrossfadeSeconds);
            }

            SuppressClipGeneration(false);
            LastStatusMessage = restoreClip ? "Realtime stream stopped." : "Realtime stream disconnected.";
        }

        private async Task SendPlaybackControlAsync(string controlType)
        {
            if (!IsConnected)
            {
                LastStatusMessage = "Realtime stream is not connected.";
                return;
            }

            await SendClientMessageAsync(new RealtimeClientMessage { type = controlType });
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

            await SendClientMessageAsync(syncMessage);
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

        private void TrySendRealtimeSteeringUpdate()
        {
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

            _ = SendClientMessageAsync(syncMessage);
            LastSentPromptSignature = promptSignature;
            lastObservedPromptSignature = promptSignature;
            lastSentSignal = currentSignal;
            lastSentParameters = currentParameters;
            LastPromptSummary = bootstrap.CurrentPromptSummary;
            LastStatusMessage = "Realtime steering update sent.";
        }

        private bool TryBuildSyncMessage(bool autoPlay, out RealtimeClientMessage message, out string promptSignature)
        {
            message = null;
            promptSignature = string.Empty;

            ResolveReferences();
            if (bootstrap == null || bootstrap.CurrentLyriaFrame == null)
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

        private async Task SendClientMessageAsync(RealtimeClientMessage message)
        {
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                LastStatusMessage = "Realtime websocket is not open.";
                return;
            }

            string json = JsonUtility.ToJson(message);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            await websocket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, socketCancellation != null ? socketCancellation.Token : CancellationToken.None);
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
                                if (inbound.sampleRate > 0 && inbound.channels > 0)
                                {
                                    pcmAudioPlayer.ConfigureFormat(inbound.sampleRate, inbound.channels);
                                }

                                pcmAudioPlayer.EnqueuePcm16(pcmBytes);
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
                    LastServerState = "connected";
                    LastStatusMessage = string.IsNullOrWhiteSpace(inbound.message) ? "Realtime bridge connected." : inbound.message;
                    SetCapabilityResult(true, "Realtime session opened successfully.", string.IsNullOrWhiteSpace(inbound.model) ? realtimeModel : inbound.model);
                    break;
                case "state":
                    LastServerState = string.IsNullOrWhiteSpace(inbound.state) ? "state" : inbound.state;
                    LastStatusMessage = string.IsNullOrWhiteSpace(inbound.message) ? LastStatusMessage : inbound.message;
                    if (string.Equals(inbound.state, "playing", StringComparison.OrdinalIgnoreCase))
                    {
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
                        IsPaused = true;
                        IsStreaming = false;
                        if (audioMixerController != null)
                        {
                            audioMixerController.SetMeditationPlaybackPaused(true);
                        }
                    }
                    else if (string.Equals(inbound.state, "stopped", StringComparison.OrdinalIgnoreCase))
                    {
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
                        IsConnected = false;
                        IsConnecting = false;
                        IsStreaming = false;
                        IsPaused = false;
                        pendingBufferedPlaybackStart = false;
                        HasBufferedPlaybackStarted = false;
                        SuppressClipGeneration(false);
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
                    LastWarningMessage = $"Filtered prompt: {inbound.filteredReason}";
                    LastStatusMessage = string.IsNullOrWhiteSpace(inbound.message) ? LastWarningMessage : inbound.message;
                    break;
                case "error":
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

            pcmAudioPlayer.AttachToMixer(audioMixerController, restartPlayback: true);
            audioMixerController.SetMeditationPlaybackPaused(false);
            HasBufferedPlaybackStarted = true;
            pendingBufferedPlaybackStart = false;
            LastStatusMessage = $"Realtime audio playback started with {BufferedSeconds:F2}s buffer.";
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
                return $"http://127.0.0.1:8000/realtime-capability?model={UnityWebRequest.EscapeURL(realtimeModel)}";
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

            using (UnityWebRequest request = UnityWebRequest.Get(BuildCapabilityUrl()))
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
