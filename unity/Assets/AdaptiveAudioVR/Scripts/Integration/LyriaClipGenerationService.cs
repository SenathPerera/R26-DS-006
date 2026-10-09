using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace AdaptiveAudioVR.Integration
{
    public class LyriaClipGenerationService : MonoBehaviour
    {
        [Header("Runtime References")]
        [SerializeField] private PrototypeBootstrap bootstrap;
        [SerializeField] private AudioMixerController audioMixerController;

        [Header("Backend Connection")]
        [SerializeField] private string backendBaseUrl = string.Empty;
        [SerializeField] private string healthEndpoint = "/health";
        [SerializeField] private string generateEndpoint = "/generate-clip";
        [SerializeField] private string preferredModel = "lyria-3-clip-preview";
        [SerializeField] private bool pingBackendOnStart = true;
        [SerializeField] private bool autoRefreshBackendHealth = true;
        [SerializeField] private float backendHealthRefreshIntervalSeconds = 8f;

        [Header("Triggering")]
        [SerializeField] private bool generateOnStart = false;
        [SerializeField] private bool autoGenerateOnActionChange = false;
        [SerializeField] private float startupDelaySeconds = 2f;
        [SerializeField] private float minimumSecondsBetweenRequests = 45f;

        [Header("Required Session Audio")]
        [SerializeField] private bool requireGeneratedClipBeforeSession = true;
        [SerializeField] private string requiredEnvironmentId = "japanese_temple_pond_garden";
        [SerializeField] private float initialGenerationRetrySeconds = 8f;

        [Header("Adaptive Regeneration")]
        [SerializeField] private bool adaptiveRegenerationEnabled = true;
        [SerializeField] private bool autoGenerateInitialClip = false;
        [SerializeField] private bool requireNormalSafetyMode = true;
        [SerializeField] private float stressDeltaThreshold = 0.14f;
        [SerializeField] private float confidenceDeltaThreshold = 0.14f;
        [SerializeField] private float aggregateParameterDeltaThreshold = 0.22f;
        [SerializeField] private float stressBucketSize = 0.10f;
        [SerializeField] private float confidenceBucketSize = 0.10f;
        [SerializeField] private float adaptiveChangeStabilitySeconds = 4f;

        [Header("Prompt Shaping")]
        [SerializeField] private bool instrumentalOnly = true;
        [SerializeField] private int maxPromptCharacters = 1400;
        [SerializeField] private bool retryWithSafePromptOnContentBlocked = true;

        [Header("Playback")]
        [SerializeField] private float meditationCrossfadeSeconds = 8f;
        [SerializeField] private bool standbyPrefetchEnabled = true;
        [SerializeField] private bool immediateApplyWhenUsingRawClip = true;
        [SerializeField] private float standbySwapWindowSeconds = 10f;

        [Header("Cache")]
        [SerializeField] private bool enablePromptCache = true;
        [SerializeField] private string cacheFolderName = "PromptCache";

        [Header("Logging")]
        [SerializeField] private bool logEventsToCsv = true;

        public bool IsGenerating { get; private set; }
        public bool IsRefreshingBackendHealth { get; private set; }
        public bool IsBackendReachable { get; private set; }
        public bool IsBackendConfigured { get; private set; }
        public bool IsBackendSdkReady { get; private set; }
        public bool UsingGeneratedMeditationClip { get; private set; }
        public bool LastRequestUsedCache { get; private set; }
        public string LastStatusMessage { get; private set; } = "Idle";
        public string LastBackendHealthSummary { get; private set; } = "Backend health not checked yet.";
        public string LastBackendError { get; private set; } = string.Empty;
        public string LastGeneratedPrompt { get; private set; } = string.Empty;
        public string LastGeneratedClipPath { get; private set; } = string.Empty;
        public string LastGeneratedModel { get; private set; } = string.Empty;
        public string LastGenerationReason { get; private set; } = "No generation yet.";
        public string LastGenerationOutcome { get; private set; } = "No generation yet.";
        public string LastCacheState { get; private set; } = "Cache idle.";
        public string LastPromptSignature { get; private set; } = string.Empty;
        public string LastPromptCacheKey { get; private set; } = string.Empty;
        public string CurrentPlaybackSourceLabel => UsingGeneratedMeditationClip ? "Generated Lyria clip" : "Raw meditation clip";
        public bool HasStandbyClip => standbyClip != null;
        public string StandbyClipPath => standbyClipPath;
        public string StandbyClipFileName => string.IsNullOrWhiteSpace(standbyClipPath) ? "none" : Path.GetFileName(standbyClipPath);
        public string StandbyStatusLabel => BuildStandbyStatusLabel();
        public float RemainingCooldownSeconds => Mathf.Max(0f, minimumSecondsBetweenRequests - (Time.time - lastRequestTimestamp));
        public float LastGenerationDurationSeconds { get; private set; }
        public int TotalGenerationRequests { get; private set; }
        public int SuccessfulGenerationCount { get; private set; }
        public int FailedGenerationCount { get; private set; }
        public int CacheHitCount { get; private set; }
        public bool IsInitialGeneratedClipReady => UsingGeneratedMeditationClip && audioMixerController != null;

        private AudioClip originalMeditationClip;
        private AudioClip standbyClip;
        private float lastRequestTimestamp = float.NegativeInfinity;
        private float nextBackendHealthRefreshTime = float.NegativeInfinity;
        private string lastAppliedPromptSignature = string.Empty;
        private string lastAppliedActionName = string.Empty;
        private string standbyClipPath = string.Empty;
        private string standbyPromptUsed = string.Empty;
        private string standbyPromptSignature = string.Empty;
        private string standbyReason = string.Empty;
        private string standbyModel = string.Empty;
        private string standbyOutcome = string.Empty;
        private bool standbyUsedCache;
        private float standbyGenerationDurationSeconds;
        private AdaptiveControllerMode lastAppliedControllerMode = AdaptiveControllerMode.Initialized;
        private GenerationRequestContext standbyContext;
        private SignalPacket lastAppliedSignal;
        private AudioParameters lastAppliedParameters;
        private string eventLogPath;
        private StreamWriter eventLogWriter;
        private Dictionary<string, CachedClipRecord> cacheIndex;
        private string pendingAdaptiveSignature = string.Empty;
        private string pendingAdaptiveReason = string.Empty;
        private float pendingAdaptiveSince = float.NegativeInfinity;

        private void Awake()
        {
            lastAppliedSignal = SignalPacket.CreateDefault();
            cacheIndex ??= new Dictionary<string, CachedClipRecord>();
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();

            if (audioMixerController != null)
            {
                originalMeditationClip = audioMixerController.CurrentMeditationClip;
            }

            UsingGeneratedMeditationClip = false;

            if (requireGeneratedClipBeforeSession)
            {
                audioMixerController?.HoldSessionPlayback();
            }

            if (pingBackendOnStart)
            {
                RequestBackendHealthRefresh();
            }

            if (requireGeneratedClipBeforeSession)
            {
                StartCoroutine(PrepareRequiredInitialClipCoroutine());
            }
            else if (generateOnStart)
            {
                StartCoroutine(GenerateAfterDelay(startupDelaySeconds));
            }
        }

        private void Update()
        {
            if (autoRefreshBackendHealth
                && !IsRefreshingBackendHealth
                && Time.time >= nextBackendHealthRefreshTime)
            {
                RequestBackendHealthRefresh();
            }

            if ((adaptiveRegenerationEnabled || autoGenerateOnActionChange)
                && bootstrap != null
                && bootstrap.IsSessionRunning)
            {
                EvaluateAdaptiveRegeneration();
            }

            if (standbyPrefetchEnabled && HasStandbyClip && ShouldSwapStandbyNow())
            {
                ApplyStandbyClip();
            }
        }

        private void OnDestroy()
        {
            CloseEventLog();
        }

        private void OnApplicationQuit()
        {
            CloseEventLog();
        }

        [ContextMenu("Generate Lyria Clip From Current Prompt")]
        public void RequestGenerationFromCurrentPrompt()
        {
            if (!TryBuildRequestContext("Manual dashboard request", out GenerationRequestContext context))
            {
                return;
            }

            RequestGeneration(context, false);
        }

        [ContextMenu("Refresh Backend Health")]
        public void RequestBackendHealthRefresh()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsRefreshingBackendHealth)
            {
                return;
            }

            StartCoroutine(RefreshBackendHealthCoroutine());
        }

        [ContextMenu("Restore Original Meditation Clip")]
        public void RestoreOriginalMeditationClip()
        {
            ResolveReferences();

            if (requireGeneratedClipBeforeSession)
            {
                LastStatusMessage = "The generated meditation requirement is active; raw clip restoration is disabled.";
                return;
            }

            if (audioMixerController == null || originalMeditationClip == null)
            {
                return;
            }

            audioMixerController.CrossfadeToMeditationClip(originalMeditationClip, meditationCrossfadeSeconds);
            UsingGeneratedMeditationClip = false;
            LastGenerationOutcome = "Restored raw meditation clip.";
            LastStatusMessage = "Restored original meditation clip.";
            LogClipEvent("restore_raw", null, false, 0f, LastGenerationOutcome, string.Empty);
        }

        private void EvaluateAdaptiveRegeneration()
        {
            ResolveReferences();

            if (bootstrap == null
                || !bootstrap.IsSessionRunning
                || !UsingGeneratedMeditationClip
                || audioMixerController == null
                || IsGenerating)
            {
                return;
            }

            if (requireNormalSafetyMode && (bootstrap.IsFallbackMode || bootstrap.CurrentSafetyMode != "Normal"))
            {
                return;
            }

            if (!IsBackendReachable || !IsBackendConfigured || !IsBackendSdkReady)
            {
                return;
            }

            if (RemainingCooldownSeconds > 0.01f)
            {
                return;
            }

            if (!TryBuildRequestContext("Adaptive request", out GenerationRequestContext context))
            {
                return;
            }

            string reason = DetermineAdaptiveReason(context);
            if (string.IsNullOrWhiteSpace(reason))
            {
                ClearPendingAdaptiveCandidate();
                return;
            }

            if (!string.Equals(context.promptSignature, pendingAdaptiveSignature, StringComparison.Ordinal))
            {
                pendingAdaptiveSignature = context.promptSignature;
                pendingAdaptiveReason = reason;
                pendingAdaptiveSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - pendingAdaptiveSince < Mathf.Max(0f, adaptiveChangeStabilitySeconds))
            {
                return;
            }

            context.reason = pendingAdaptiveReason;
            ClearPendingAdaptiveCandidate();
            RequestGeneration(context, true);
        }

        private string DetermineAdaptiveReason(GenerationRequestContext context)
        {
            bool hasGeneratedClip = UsingGeneratedMeditationClip || !string.IsNullOrWhiteSpace(lastAppliedPromptSignature);
            if (!hasGeneratedClip)
            {
                return autoGenerateInitialClip ? "Initial adaptive Lyria clip" : string.Empty;
            }

            if (context.promptSignature == lastAppliedPromptSignature)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(standbyPromptSignature) && context.promptSignature == standbyPromptSignature)
            {
                return string.Empty;
            }

            bool actionChanged = !string.Equals(context.actionName, lastAppliedActionName, StringComparison.Ordinal);
            bool modeChanged = context.controllerMode != lastAppliedControllerMode;
            float stressDelta = Mathf.Abs(context.signal.stress - lastAppliedSignal.stress);
            float confidenceDelta = Mathf.Abs(context.signal.confidence - lastAppliedSignal.confidence);
            float parameterDelta = ComputeAggregateParameterDelta(context.parameters, lastAppliedParameters);

            if ((actionChanged || modeChanged) && parameterDelta >= aggregateParameterDeltaThreshold * 0.55f)
            {
                return $"Adaptive refresh because action changed to '{context.actionName}' with parameter delta {parameterDelta:F2}.";
            }

            if (stressDelta >= stressDeltaThreshold
                && confidenceDelta >= confidenceDeltaThreshold
                && parameterDelta >= aggregateParameterDeltaThreshold)
            {
                return $"Adaptive refresh because stress/confidence moved by {stressDelta:F2}/{confidenceDelta:F2}.";
            }

            if (stressDelta >= stressDeltaThreshold * 1.4f
                && parameterDelta >= aggregateParameterDeltaThreshold * 0.75f)
            {
                return $"Adaptive refresh because stress moved by {stressDelta:F2} and audio parameters drifted by {parameterDelta:F2}.";
            }

            return string.Empty;
        }

        private bool TryBuildRequestContext(string requestedReason, out GenerationRequestContext context)
        {
            context = default;

            ResolveReferences();
            if (bootstrap == null || audioMixerController == null)
            {
                LastStatusMessage = "Generation unavailable because references are missing.";
                return false;
            }

            LyriaControlFrame frame = bootstrap.CurrentLyriaFrame;
            if (frame == null)
            {
                LastStatusMessage = "No Lyria control frame is available yet.";
                return false;
            }

            frame.Normalize();
            if (requireGeneratedClipBeforeSession
                && !string.Equals(frame.environmentId, requiredEnvironmentId, StringComparison.OrdinalIgnoreCase))
            {
                LastStatusMessage = $"Generation is blocked because environment '{frame.environmentId}' does not match required environment '{requiredEnvironmentId}'.";
                return false;
            }

            SignalPacket signal = bootstrap.CurrentSignal;
            AudioParameters parameters = bootstrap.CurrentParameters;
            string prompt = BuildGenerationPrompt(frame);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                LastStatusMessage = "Prompt generation failed.";
                return false;
            }

            string signature = BuildPromptSignature(frame, signal, parameters);
            string model = string.IsNullOrWhiteSpace(preferredModel) ? "lyria-3-clip-preview" : preferredModel.Trim();
            string cacheKey = BuildPromptCacheKey(model, signature);

            context = new GenerationRequestContext
            {
                frame = frame,
                prompt = prompt,
                promptSignature = signature,
                cacheKey = cacheKey,
                model = model,
                reason = requestedReason,
                actionName = bootstrap.CurrentActionName,
                controllerMode = bootstrap.CurrentControllerMode,
                safetyMode = bootstrap.CurrentSafetyMode,
                signal = signal,
                parameters = parameters
            };

            return true;
        }

        private void RequestGeneration(GenerationRequestContext context, bool automatic)
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                LastStatusMessage = "Play Mode is required before requesting a generated clip.";
                return;
            }

            if (IsGenerating)
            {
                LastStatusMessage = "Generation already running.";
                return;
            }

            if (RemainingCooldownSeconds > 0.01f)
            {
                LastStatusMessage = automatic
                    ? $"Adaptive generation deferred during cooldown ({RemainingCooldownSeconds:F1}s remaining)."
                    : $"Generation cooling down for {RemainingCooldownSeconds:F1}s.";
                return;
            }

            bool stageAsStandby = automatic && standbyPrefetchEnabled && ShouldStageAsStandby();
            StartCoroutine(GenerateCurrentClipCoroutine(context, stageAsStandby));
        }

        private IEnumerator GenerateAfterDelay(float delaySeconds)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }

            if (TryBuildRequestContext("Startup request", out GenerationRequestContext context))
            {
                RequestGeneration(context, false);
            }
        }

        private IEnumerator PrepareRequiredInitialClipCoroutine()
        {
            if (startupDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(startupDelaySeconds);
            }

            while (isActiveAndEnabled && !UsingGeneratedMeditationClip)
            {
                ResolveReferences();
                if (bootstrap == null || audioMixerController == null || !bootstrap.IsPrepared)
                {
                    yield return null;
                    continue;
                }

                audioMixerController.HoldSessionPlayback();
                if (TryBuildRequestContext("Required personalized session clip", out GenerationRequestContext context))
                {
                    yield return GenerateCurrentClipCoroutine(context, false);
                }

                if (UsingGeneratedMeditationClip)
                {
                    bootstrap.TryBeginPreparedSession();
                    yield break;
                }

                LastStatusMessage = "Waiting to retry personalized meditation generation.";
                yield return new WaitForSecondsRealtime(Mathf.Max(2f, initialGenerationRetrySeconds));
            }
        }

        private IEnumerator GenerateCurrentClipCoroutine(GenerationRequestContext context, bool stageAsStandby)
        {
            ResolveReferences();

            if (bootstrap == null || audioMixerController == null)
            {
                LastStatusMessage = "Generation unavailable because references are missing.";
                yield break;
            }

            IsGenerating = true;
            TotalGenerationRequests++;
            LastRequestUsedCache = false;
            LastGenerationReason = context.reason;
            LastPromptSignature = context.promptSignature;
            LastPromptCacheKey = context.cacheKey;
            LastBackendError = string.Empty;
            LastStatusMessage = stageAsStandby
                ? "Preparing standby meditation clip..."
                : "Preparing adaptive meditation clip...";
            float startedAt = Time.realtimeSinceStartup;
            string promptUsed = context.prompt;

            if (enablePromptCache)
            {
                bool cacheLoaded = false;
                yield return TryLoadCachedClipCoroutine(context, stageAsStandby, loaded => cacheLoaded = loaded);
                if (cacheLoaded)
                {
                    IsGenerating = false;
                    yield break;
                }
            }

            LastCacheState = "Cache miss. Requesting backend generation.";
            LastStatusMessage = stageAsStandby
                ? "Requesting standby generated clip from Lyria..."
                : "Requesting generated meditation clip from Lyria...";

            BackendGenerationAttemptResult attempt = null;
            yield return RequestBackendClipCoroutine(context, context.prompt, result => attempt = result);

            if (!attempt.success && retryWithSafePromptOnContentBlocked && IsContentBlockedError(attempt.errorDetail))
            {
                string safePrompt = BuildSafeFallbackPrompt(context.frame);
                promptUsed = safePrompt;
                LastStatusMessage = "Primary prompt was blocked. Retrying with safe fallback prompt...";
                yield return RequestBackendClipCoroutine(context, safePrompt, result => attempt = result);
                if (attempt.success)
                {
                    LastGenerationOutcome = stageAsStandby
                        ? "Prepared standby clip after safe fallback prompt retry."
                        : "Generated fresh clip after safe fallback prompt retry.";
                }
            }

            if (!attempt.success)
            {
                HandleGenerationFailure(
                    context,
                    attempt.statusMessage,
                    attempt.errorDetail,
                    Time.realtimeSinceStartup - startedAt);
                IsGenerating = false;
                yield break;
            }

            string outputPath = WriteGeneratedAudio(attempt.audioBytes, attempt.savedFileName);
            WritePromptCache(context, attempt.audioBytes, outputPath);

            bool completed = false;
            if (stageAsStandby)
            {
                yield return LoadAndStageClipCoroutine(
                    outputPath,
                    context,
                    usedCache: false,
                    generationDuration: Time.realtimeSinceStartup - startedAt,
                    promptUsedOverride: promptUsed,
                    onCompleted: value => completed = value);
            }
            else
            {
                yield return LoadAndApplyClipCoroutine(
                    outputPath,
                    context,
                    usedCache: false,
                    generationDuration: Time.realtimeSinceStartup - startedAt,
                    onApplied: value => completed = value,
                    promptUsedOverride: promptUsed);
            }

            if (!completed)
            {
                HandleGenerationFailure(context, "Generated MP3 could not be loaded.", LastBackendError, Time.realtimeSinceStartup - startedAt);
            }

            IsGenerating = false;
        }

        private IEnumerator RequestBackendClipCoroutine(GenerationRequestContext context, string prompt, Action<BackendGenerationAttemptResult> onFinished)
        {
            var payload = new LyriaClipRequest
            {
                prompt = prompt,
                model = context.model,
                requestId = Guid.NewGuid().ToString("N"),
                instrumentalOnly = instrumentalOnly
            };

            var result = new BackendGenerationAttemptResult();
            byte[] requestBody = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using (var request = new UnityWebRequest(GetBackendUrl(generateEndpoint), UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(requestBody);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    result.success = false;
                    result.statusMessage = $"Generation request failed: {request.error}";
                    result.errorDetail = responseText;
                    onFinished?.Invoke(result);
                    yield break;
                }

                LyriaClipResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<LyriaClipResponse>(responseText);
                }
                catch (Exception ex)
                {
                    result.success = false;
                    result.statusMessage = "Generation response could not be parsed.";
                    result.errorDetail = ex.Message;
                    onFinished?.Invoke(result);
                    yield break;
                }

                if (response == null || !response.success || string.IsNullOrWhiteSpace(response.audioBase64))
                {
                    result.success = false;
                    result.statusMessage = "Generation failed.";
                    result.errorDetail = response != null && !string.IsNullOrWhiteSpace(response.errorMessage)
                        ? response.errorMessage
                        : responseText;
                    onFinished?.Invoke(result);
                    yield break;
                }

                try
                {
                    result.audioBytes = Convert.FromBase64String(response.audioBase64);
                }
                catch (Exception ex)
                {
                    result.success = false;
                    result.statusMessage = "Generated audio was not valid base64.";
                    result.errorDetail = ex.Message;
                    onFinished?.Invoke(result);
                    yield break;
                }

                result.success = true;
                result.savedFileName = response.savedFileName;
                onFinished?.Invoke(result);
            }
        }

        private IEnumerator TryLoadCachedClipCoroutine(GenerationRequestContext context, bool stageAsStandby, Action<bool> onFinished)
        {
            string cachePath = GetCacheAudioPath(context.model, context.cacheKey);
            if (!File.Exists(cachePath))
            {
                LastCacheState = $"Cache miss for key {context.cacheKey}.";
                onFinished?.Invoke(false);
                yield break;
            }

            LastCacheState = $"Cache hit for key {context.cacheKey}.";
            bool applied = false;
            if (stageAsStandby)
            {
                yield return LoadAndStageClipCoroutine(
                    cachePath,
                    context,
                    usedCache: true,
                    generationDuration: 0f,
                    promptUsedOverride: context.prompt,
                    onCompleted: value => applied = value);
            }
            else
            {
                yield return LoadAndApplyClipCoroutine(
                    cachePath,
                    context,
                    usedCache: true,
                    generationDuration: 0f,
                    onApplied: value => applied = value);
            }

            if (applied)
            {
                CacheHitCount++;
            }

            onFinished?.Invoke(applied);
        }

        private IEnumerator LoadAndApplyClipCoroutine(
            string clipPath,
            GenerationRequestContext context,
            bool usedCache,
            float generationDuration,
            Action<bool> onApplied,
            string promptUsedOverride = null)
        {
            AudioClip newClip = null;
            bool loaded = false;
            yield return LoadClipFromPathCoroutine(clipPath, clip => newClip = clip, success => loaded = success);
            if (!loaded || newClip == null)
            {
                onApplied?.Invoke(false);
                yield break;
            }

            ApplyLoadedClip(newClip, clipPath, context, usedCache, generationDuration, promptUsedOverride);
            onApplied?.Invoke(true);
        }

        private IEnumerator LoadAndStageClipCoroutine(
            string clipPath,
            GenerationRequestContext context,
            bool usedCache,
            float generationDuration,
            string promptUsedOverride,
            Action<bool> onCompleted)
        {
            AudioClip newClip = null;
            bool loaded = false;
            yield return LoadClipFromPathCoroutine(clipPath, clip => newClip = clip, success => loaded = success);
            if (!loaded || newClip == null)
            {
                onCompleted?.Invoke(false);
                yield break;
            }

            StageStandbyClip(newClip, clipPath, context, usedCache, generationDuration, promptUsedOverride);
            onCompleted?.Invoke(true);
        }

        private IEnumerator LoadClipFromPathCoroutine(string clipPath, Action<AudioClip> onLoaded, Action<bool> onFinished)
        {
            using (var loadRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(clipPath).AbsoluteUri, AudioType.MPEG))
            {
                if (loadRequest.downloadHandler is DownloadHandlerAudioClip audioClipHandler)
                {
                    audioClipHandler.streamAudio = false;
                }

                yield return loadRequest.SendWebRequest();

                if (loadRequest.result != UnityWebRequest.Result.Success)
                {
                    LastBackendError = loadRequest.error;
                    LastStatusMessage = $"Audio clip load failed: {loadRequest.error}";
                    onFinished?.Invoke(false);
                    yield break;
                }

                AudioClip newClip = DownloadHandlerAudioClip.GetContent(loadRequest);
                if (newClip == null)
                {
                    LastBackendError = "Decoded audio clip was null.";
                    LastStatusMessage = "Decoded audio clip was null.";
                    onFinished?.Invoke(false);
                    yield break;
                }

                newClip.name = Path.GetFileNameWithoutExtension(clipPath);
                onLoaded?.Invoke(newClip);
                onFinished?.Invoke(true);
            }
        }

        private void ApplyLoadedClip(
            AudioClip newClip,
            string clipPath,
            GenerationRequestContext context,
            bool usedCache,
            float generationDuration,
            string promptUsedOverride)
        {
            AudioClip previousClip = audioMixerController.CurrentMeditationClip;
            bool sessionIsWaitingForInitialClip = requireGeneratedClipBeforeSession
                                                  && bootstrap != null
                                                  && !bootstrap.IsSessionRunning;
            if (sessionIsWaitingForInitialClip)
            {
                audioMixerController.ReplaceMeditationClip(newClip, false);
            }
            else
            {
                bool clipChanged = audioMixerController.CrossfadeToMeditationClip(newClip, meditationCrossfadeSeconds);
                if (!clipChanged)
                {
                    audioMixerController.ReplaceMeditationClip(newClip);
                }
            }

            if (previousClip != null
                && previousClip != originalMeditationClip
                && previousClip != newClip)
            {
                StartCoroutine(DestroyClipAfterDelay(previousClip, meditationCrossfadeSeconds + 1f));
            }

            UsingGeneratedMeditationClip = true;
            LastRequestUsedCache = usedCache;
            LastGeneratedPrompt = string.IsNullOrWhiteSpace(promptUsedOverride) ? context.prompt : promptUsedOverride;
            LastGeneratedClipPath = clipPath;
            LastGeneratedModel = context.model;
            LastGenerationDurationSeconds = generationDuration;
            if (string.IsNullOrWhiteSpace(LastGenerationOutcome) || usedCache)
            {
                LastGenerationOutcome = usedCache ? "Loaded cached clip and crossfaded safely." : "Generated fresh clip and crossfaded safely.";
            }

            LastStatusMessage = usedCache
                ? $"Loaded cached clip: {Path.GetFileName(clipPath)}"
                : $"Generated clip loaded: {Path.GetFileName(clipPath)}";

            SuccessfulGenerationCount++;
            lastRequestTimestamp = Time.time;
            lastAppliedPromptSignature = context.promptSignature;
            lastAppliedActionName = context.actionName;
            lastAppliedControllerMode = context.controllerMode;
            lastAppliedSignal = context.signal;
            lastAppliedParameters = context.parameters;

            CacheOrUpdateRecord(context, clipPath);
            LogClipEvent(
                usedCache ? "cache_hit" : "backend_generate",
                context,
                usedCache,
                generationDuration,
                LastGenerationOutcome,
                string.Empty);

            if (sessionIsWaitingForInitialClip)
            {
                bootstrap.TryBeginPreparedSession();
            }
        }

        private void StageStandbyClip(
            AudioClip newClip,
            string clipPath,
            GenerationRequestContext context,
            bool usedCache,
            float generationDuration,
            string promptUsedOverride)
        {
            if (standbyClip != null && standbyClip != newClip)
            {
                Destroy(standbyClip);
            }

            standbyClip = newClip;
            standbyClipPath = clipPath;
            standbyPromptSignature = context.promptSignature;
            standbyPromptUsed = string.IsNullOrWhiteSpace(promptUsedOverride) ? context.prompt : promptUsedOverride;
            standbyReason = context.reason;
            standbyModel = context.model;
            standbyUsedCache = usedCache;
            standbyGenerationDurationSeconds = generationDuration;
            standbyOutcome = usedCache ? "Prepared cached standby clip." : "Prepared fresh standby clip.";
            standbyContext = context;

            LastRequestUsedCache = usedCache;
            LastGeneratedPrompt = standbyPromptUsed;
            LastGeneratedClipPath = clipPath;
            LastGeneratedModel = context.model;
            LastGenerationDurationSeconds = generationDuration;
            LastGenerationOutcome = standbyOutcome;
            LastStatusMessage = $"Standby clip ready: {Path.GetFileName(clipPath)}";

            SuccessfulGenerationCount++;
            lastRequestTimestamp = Time.time;

            CacheOrUpdateRecord(context, clipPath);
            LogClipEvent(
                usedCache ? "standby_cache_ready" : "standby_ready",
                context,
                usedCache,
                generationDuration,
                standbyOutcome,
                string.Empty);
        }

        private bool ApplyStandbyClip()
        {
            if (standbyClip == null || audioMixerController == null)
            {
                return false;
            }

            AudioClip clipToApply = standbyClip;
            string clipPath = standbyClipPath;
            string promptUsed = standbyPromptUsed;
            string model = standbyModel;
            bool usedCache = standbyUsedCache;
            float generationDuration = standbyGenerationDurationSeconds;
            GenerationRequestContext context = standbyContext;

            standbyClip = null;
            standbyClipPath = string.Empty;
            standbyPromptSignature = string.Empty;
            standbyPromptUsed = string.Empty;
            standbyReason = string.Empty;
            standbyModel = string.Empty;
            standbyOutcome = string.Empty;
            standbyUsedCache = false;
            standbyGenerationDurationSeconds = 0f;

            AudioClip previousClip = audioMixerController.CurrentMeditationClip;
            bool clipChanged = audioMixerController.CrossfadeToMeditationClip(clipToApply, meditationCrossfadeSeconds);
            if (!clipChanged)
            {
                audioMixerController.ReplaceMeditationClip(clipToApply);
            }

            if (previousClip != null
                && previousClip != originalMeditationClip
                && previousClip != clipToApply)
            {
                StartCoroutine(DestroyClipAfterDelay(previousClip, meditationCrossfadeSeconds + 1f));
            }

            UsingGeneratedMeditationClip = true;
            LastRequestUsedCache = usedCache;
            LastGeneratedPrompt = promptUsed;
            LastGeneratedClipPath = clipPath;
            LastGeneratedModel = model;
            LastGenerationDurationSeconds = generationDuration;
            LastGenerationReason = context.reason;
            LastGenerationOutcome = usedCache
                ? "Applied cached standby clip at loop boundary."
                : "Applied standby clip at loop boundary.";
            LastStatusMessage = $"Standby clip applied: {Path.GetFileName(clipPath)}";

            lastAppliedPromptSignature = context.promptSignature;
            lastAppliedActionName = context.actionName;
            lastAppliedControllerMode = context.controllerMode;
            lastAppliedSignal = context.signal;
            lastAppliedParameters = context.parameters;

            LogClipEvent(
                "standby_swap",
                context,
                usedCache,
                generationDuration,
                LastGenerationOutcome,
                string.Empty);
            return true;
        }

        private IEnumerator RefreshBackendHealthCoroutine()
        {
            IsRefreshingBackendHealth = true;
            LastBackendHealthSummary = "Checking backend...";

            using (var request = UnityWebRequest.Get(GetBackendUrl(healthEndpoint)))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    IsBackendReachable = false;
                    IsBackendConfigured = false;
                    IsBackendSdkReady = false;
                    LastBackendHealthSummary = $"Backend offline: {request.error}";
                    LastBackendError = request.error;
                    nextBackendHealthRefreshTime = Time.time + Mathf.Max(2f, backendHealthRefreshIntervalSeconds);
                    IsRefreshingBackendHealth = false;
                    yield break;
                }

                BackendHealthResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<BackendHealthResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    IsBackendReachable = false;
                    IsBackendConfigured = false;
                    IsBackendSdkReady = false;
                    LastBackendHealthSummary = $"Backend health parse failed: {ex.Message}";
                    LastBackendError = ex.Message;
                    nextBackendHealthRefreshTime = Time.time + Mathf.Max(2f, backendHealthRefreshIntervalSeconds);
                    IsRefreshingBackendHealth = false;
                    yield break;
                }

                IsBackendReachable = response != null && string.Equals(response.status, "ok", StringComparison.OrdinalIgnoreCase);
                IsBackendConfigured = response != null && response.apiKeyConfigured;
                IsBackendSdkReady = response != null && response.sdkReady;
                LastBackendError = string.Empty;
                LastBackendHealthSummary =
                    response == null
                        ? "Backend returned an empty health response."
                        : $"Backend {response.status} | API key {(response.apiKeyConfigured ? "ready" : "missing")} | SDK {(response.sdkReady ? "ready" : "missing")} | Model {response.defaultModel}";
            }

            nextBackendHealthRefreshTime = Time.time + Mathf.Max(2f, backendHealthRefreshIntervalSeconds);
            IsRefreshingBackendHealth = false;
        }

        private void HandleGenerationFailure(GenerationRequestContext context, string statusMessage, string errorDetail, float generationDuration)
        {
            FailedGenerationCount++;
            LastRequestUsedCache = false;
            LastGenerationDurationSeconds = generationDuration;
            LastBackendError = errorDetail;
            LastStatusMessage = statusMessage;
            bool sessionIsGated = requireGeneratedClipBeforeSession && (bootstrap == null || !bootstrap.IsSessionRunning);
            LastGenerationOutcome = sessionIsGated
                ? "Generation failed; session remains stopped until personalized generated audio is ready."
                : "Generation failed; kept the current generated clip.";
            LastCacheState = sessionIsGated ? "Session start remains gated." : "Continuing current generated clip.";

            Debug.LogError($"[LyriaClipGenerationService] {statusMessage} {errorDetail}", this);
            LogClipEvent("generation_failed", context, false, generationDuration, LastGenerationOutcome, errorDetail);
        }

        private string WriteGeneratedAudio(byte[] audioBytes, string suggestedFileName)
        {
            string generatedDirectory = GetGeneratedAudioDirectory();
            Directory.CreateDirectory(generatedDirectory);

            string safeFileName = string.IsNullOrWhiteSpace(suggestedFileName)
                ? $"lyria_clip_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp3"
                : SanitizeFileName(suggestedFileName);

            string outputPath = Path.Combine(generatedDirectory, safeFileName);
            File.WriteAllBytes(outputPath, audioBytes);
            return outputPath;
        }

        private void WritePromptCache(GenerationRequestContext context, byte[] audioBytes, string sourcePath)
        {
            if (!enablePromptCache)
            {
                return;
            }

            string cachePath = GetCacheAudioPath(context.model, context.cacheKey);
            string cacheDirectory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }

            File.WriteAllBytes(cachePath, audioBytes);

            var metadata = new CachedClipMetadata
            {
                cacheKey = context.cacheKey,
                promptSignature = context.promptSignature,
                prompt = context.prompt,
                model = context.model,
                reason = context.reason,
                sourceFilePath = sourcePath,
                cachedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };

            File.WriteAllText(GetCacheMetadataPath(context.model, context.cacheKey), JsonUtility.ToJson(metadata, true), Encoding.UTF8);
            LastCacheState = $"Stored clip in cache as {context.cacheKey}.";
        }

        private void CacheOrUpdateRecord(GenerationRequestContext context, string clipPath)
        {
            cacheIndex ??= new Dictionary<string, CachedClipRecord>();
            cacheIndex[context.cacheKey] = new CachedClipRecord
            {
                cacheKey = context.cacheKey,
                promptSignature = context.promptSignature,
                clipPath = clipPath,
                model = context.model
            };
        }

        private string BuildGenerationPrompt(LyriaControlFrame frame)
        {
            frame.Normalize();

            var builder = new StringBuilder();
            builder.Append("Create a 30-second instrumental meditation loop. ");
            builder.Append($"It must musically fit the {frame.environmentDisplayName} VR environment. ");
            builder.Append("Calm, smooth, seamless, relaxing, and non-distracting. ");
            builder.Append("No vocals. Light or no percussion. Soft transients. Gentle consonant harmony. ");
            builder.Append("Style focus: ");

            if (frame.weightedPrompts != null && frame.weightedPrompts.Length > 0)
            {
                string focusList = string.Join(
                    ", ",
                    frame.weightedPrompts
                        .OrderByDescending(prompt => prompt.weight)
                        .Take(6)
                        .Select(prompt => SanitizePromptToken(prompt.text)));
                builder.Append(focusList);
                builder.Append(". ");
            }

            builder.AppendFormat(CultureInfo.InvariantCulture, "Target tempo around {0} BPM. ", frame.config.bpm);
            builder.AppendFormat(CultureInfo.InvariantCulture, "Texture density {0}, brightness {1}. ",
                DescribeDensity(frame.config.density),
                DescribeBrightness(frame.config.brightness));
            builder.Append($"Musical scale {frame.config.scale}. ");

            if (frame.config.muteDrums)
            {
                builder.Append("Keep drums absent or extremely restrained. ");
            }

            if (frame.config.muteBass)
            {
                builder.Append("Keep bass very subtle. ");
            }

            if (instrumentalOnly)
            {
                builder.Append("Instrumental only. ");
            }

            string prompt = builder.ToString().Trim();
            return prompt.Length <= maxPromptCharacters ? prompt : prompt.Substring(0, maxPromptCharacters).TrimEnd();
        }

        private string BuildSafeFallbackPrompt(LyriaControlFrame frame)
        {
            frame.Normalize();

            string topFocus = frame.weightedPrompts != null && frame.weightedPrompts.Length > 0
                ? string.Join(", ",
                    frame.weightedPrompts
                        .OrderByDescending(prompt => prompt.weight)
                        .Take(3)
                        .Select(prompt => SanitizePromptToken(prompt.text)))
                : "calm piano, soft pad, gentle ambient texture";

            string prompt =
                $"Create a 30-second instrumental ambient meditation loop. " +
                $"It must musically fit the {frame.environmentDisplayName} VR environment. " +
                $"Peaceful, gentle, relaxing, seamless, no vocals, no strong percussion. " +
                $"Use {topFocus}. " +
                $"Slow tempo around {frame.config.bpm} BPM. " +
                $"Soft brightness, moderate texture, smooth calming mood.";

            return prompt.Length <= maxPromptCharacters ? prompt : prompt.Substring(0, maxPromptCharacters).TrimEnd();
        }

        private string BuildPromptSignature(LyriaControlFrame frame, SignalPacket signal, AudioParameters parameters)
        {
            frame.Normalize();

            var builder = new StringBuilder();
            builder.Append(frame.environmentId.Trim().ToLowerInvariant());
            builder.Append('|');
            builder.Append(frame.strategyName.Trim().ToLowerInvariant());
            builder.Append('|');
            builder.Append(frame.actionName.Trim().ToLowerInvariant());
            builder.Append('|');
            builder.Append(bootstrap != null ? bootstrap.CurrentControllerMode.ToString() : "Unknown");
            builder.Append('|');
            builder.Append(Quantize(signal.stress, stressBucketSize).ToString("F2", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Quantize(signal.confidence, confidenceBucketSize).ToString("F2", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Quantize(parameters.intensity, 0.10f).ToString("F2", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Quantize(parameters.density, 0.10f).ToString("F2", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Quantize(parameters.brightness, 0.10f).ToString("F2", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Quantize(parameters.ambientMix, 0.10f).ToString("F2", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Quantize(parameters.musicMix, 0.10f).ToString("F2", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Mathf.RoundToInt(frame.config.bpm / 6f) * 6);
            builder.Append('|');

            if (frame.weightedPrompts != null)
            {
                foreach (PromptWeight prompt in frame.weightedPrompts.OrderByDescending(item => item.weight).Take(6))
                {
                    builder.Append(prompt.text.Trim().ToLowerInvariant());
                    builder.Append(';');
                }
            }

            return builder.ToString();
        }

        private void ClearPendingAdaptiveCandidate()
        {
            pendingAdaptiveSignature = string.Empty;
            pendingAdaptiveReason = string.Empty;
            pendingAdaptiveSince = float.NegativeInfinity;
        }

        private static string BuildPromptCacheKey(string model, string signature)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{model}|{signature}"));
                return BitConverter.ToString(hash, 0, 12).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static float ComputeAggregateParameterDelta(AudioParameters current, AudioParameters previous)
        {
            return Mathf.Abs(current.intensity - previous.intensity)
                   + Mathf.Abs(current.density - previous.density)
                   + Mathf.Abs(current.brightness - previous.brightness)
                   + Mathf.Abs(current.ambientMix - previous.ambientMix)
                   + Mathf.Abs(current.musicMix - previous.musicMix);
        }

        private static float Quantize(float value, float step)
        {
            if (step <= 0.0001f)
            {
                return value;
            }

            return Mathf.Round(value / step) * step;
        }

        private bool ShouldStageAsStandby()
        {
            if (!standbyPrefetchEnabled || audioMixerController == null)
            {
                return false;
            }

            if (immediateApplyWhenUsingRawClip && !UsingGeneratedMeditationClip)
            {
                return false;
            }

            AudioClip currentClip = audioMixerController.CurrentMeditationClip;
            return currentClip != null && audioMixerController.IsMeditationPlaying;
        }

        private bool ShouldSwapStandbyNow()
        {
            if (standbyClip == null || audioMixerController == null)
            {
                return false;
            }

            if (!UsingGeneratedMeditationClip && immediateApplyWhenUsingRawClip)
            {
                return true;
            }

            if (!audioMixerController.IsMeditationPlaying)
            {
                return true;
            }

            if (audioMixerController.CurrentMeditationClipLengthSeconds <= 0.01f)
            {
                return true;
            }

            return audioMixerController.CurrentMeditationTimeRemainingSeconds <= Mathf.Max(0.25f, standbySwapWindowSeconds);
        }

        private string BuildStandbyStatusLabel()
        {
            if (HasStandbyClip)
            {
                return $"Standby ready: {StandbyClipFileName}";
            }

            if (standbyPrefetchEnabled)
            {
                return "Standby idle.";
            }

            return "Standby disabled.";
        }

        private static string DescribeDensity(float density)
        {
            if (density <= 0.28f)
            {
                return "sparse";
            }

            if (density >= 0.68f)
            {
                return "moderately layered";
            }

            return "balanced";
        }

        private static string DescribeBrightness(float brightness)
        {
            if (brightness <= 0.30f)
            {
                return "soft";
            }

            if (brightness >= 0.70f)
            {
                return "clear";
            }

            return "neutral";
        }

        private static string SanitizePromptToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "gentle ambient texture";
            }

            var builder = new StringBuilder(text.Length);
            foreach (char character in text.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character) || character == ' ' || character == '-')
                {
                    builder.Append(character);
                }
            }

            string cleaned = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return "gentle ambient texture";
            }

            cleaned = cleaned.Replace("imported", string.Empty).Replace("policy", string.Empty).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "gentle ambient texture" : cleaned;
        }

        private static bool IsContentBlockedError(string errorDetail)
        {
            if (string.IsNullOrWhiteSpace(errorDetail))
            {
                return false;
            }

            return errorDetail.IndexOf("content_blocked", StringComparison.OrdinalIgnoreCase) >= 0
                   || errorDetail.IndexOf("policy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ResolveReferences()
        {
            bootstrap ??= FindAnyObjectByType<PrototypeBootstrap>();
            audioMixerController ??= FindAnyObjectByType<AudioMixerController>();
        }

        private string GetBackendUrl(string endpoint)
        {
            return $"{backendBaseUrl.TrimEnd('/')}{endpoint}";
        }

        private string GetGeneratedAudioDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "GeneratedAudio");
        }

        private string GetCacheRootDirectory()
        {
            return Path.Combine(GetGeneratedAudioDirectory(), cacheFolderName);
        }

        private string GetCacheAudioPath(string model, string cacheKey)
        {
            return Path.Combine(GetCacheRootDirectory(), $"{SanitizeFileNameFragment(model)}_{cacheKey}.mp3");
        }

        private string GetCacheMetadataPath(string model, string cacheKey)
        {
            return Path.Combine(GetCacheRootDirectory(), $"{SanitizeFileNameFragment(model)}_{cacheKey}.json");
        }

        private IEnumerator DestroyClipAfterDelay(AudioClip clip, float delaySeconds)
        {
            if (clip == null || clip == originalMeditationClip)
            {
                yield break;
            }

            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }

            Destroy(clip);
        }

        private void LogClipEvent(
            string eventType,
            GenerationRequestContext? context,
            bool usedCache,
            float durationSeconds,
            string outcome,
            string error)
        {
            if (!logEventsToCsv)
            {
                return;
            }

            EnsureEventLog();
            if (eventLogWriter == null)
            {
                return;
            }

            GenerationRequestContext record = context.GetValueOrDefault();
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:F3},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12:F3},{13:F3},{14:F3},{15:F3},{16:F3},{17:F3},{18:F3},{19},{20},{21},{22}",
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Time.time,
                SanitizeCsv(eventType),
                SanitizeCsv(record.reason),
                SanitizeCsv(outcome),
                usedCache,
                SanitizeCsv(record.cacheKey),
                SanitizeCsv(record.promptSignature),
                SanitizeCsv(record.model),
                SanitizeCsv(record.actionName),
                SanitizeCsv(record.controllerMode.ToString()),
                SanitizeCsv(record.safetyMode),
                record.signal.stress,
                record.signal.confidence,
                record.parameters.intensity,
                record.parameters.density,
                record.parameters.brightness,
                record.parameters.ambientMix,
                record.parameters.musicMix,
                durationSeconds,
                SanitizeCsv(LastGeneratedClipPath),
                SanitizeCsv(error),
                SanitizeCsv(CurrentPlaybackSourceLabel));

            try
            {
                eventLogWriter.WriteLine(line);
                eventLogWriter.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LyriaClipGenerationService] Failed to write clip event log: {ex.Message}", this);
            }
        }

        private void EnsureEventLog()
        {
            if (eventLogWriter != null)
            {
                return;
            }

            try
            {
                string logDirectory = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(logDirectory);
                eventLogPath = Path.Combine(logDirectory, $"lyria_clip_events_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                eventLogWriter = new StreamWriter(eventLogPath, false, Encoding.UTF8);
                eventLogWriter.WriteLine("timestampUtc,unityTime,eventType,reason,outcome,usedCache,cacheKey,promptSignature,model,actionName,controllerMode,safetyMode,stress,confidence,intensity,density,brightness,ambientMix,musicMix,durationSeconds,clipPath,error,playbackSource");
                eventLogWriter.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LyriaClipGenerationService] Failed to open clip event log: {ex.Message}", this);
                eventLogWriter = null;
            }
        }

        private void CloseEventLog()
        {
            if (eventLogWriter == null)
            {
                return;
            }

            try
            {
                eventLogWriter.Flush();
                eventLogWriter.Dispose();
            }
            catch
            {
                // Ignore shutdown IO errors.
            }
            finally
            {
                eventLogWriter = null;
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return $"lyria_clip_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp3";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(fileName.Length);
            foreach (char character in fileName)
            {
                builder.Append(invalidChars.Contains(character) ? '_' : character);
            }

            string sanitized = builder.ToString();
            if (!sanitized.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                sanitized += ".mp3";
            }

            return sanitized;
        }

        private static string SanitizeFileNameFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown_model";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                builder.Append(invalidChars.Contains(character) ? '_' : character);
            }

            return builder.ToString().Replace(' ', '_');
        }

        private static string SanitizeCsv(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            return value.Replace(",", "_").Replace("\r", " ").Replace("\n", " ");
        }

        [Serializable]
        private struct GenerationRequestContext
        {
            public LyriaControlFrame frame;
            public string prompt;
            public string promptSignature;
            public string cacheKey;
            public string model;
            public string reason;
            public string actionName;
            public AdaptiveControllerMode controllerMode;
            public string safetyMode;
            public SignalPacket signal;
            public AudioParameters parameters;
        }

        [Serializable]
        private class LyriaClipRequest
        {
            public string prompt;
            public string model;
            public string requestId;
            public bool instrumentalOnly;
        }

        [Serializable]
        private class LyriaClipResponse
        {
            public bool success = false;
            public string requestId = string.Empty;
            public string model = string.Empty;
            public string promptUsed = string.Empty;
            public string lyrics = string.Empty;
            public string audioBase64 = string.Empty;
            public string mimeType = string.Empty;
            public string savedFileName = string.Empty;
            public string generatedAtUtc = string.Empty;
            public string errorMessage = string.Empty;
        }

        [Serializable]
        private class BackendHealthResponse
        {
            public string status = string.Empty;
            public bool sdkReady = false;
            public bool apiKeyConfigured = false;
            public string defaultModel = string.Empty;
        }

        [Serializable]
        private class CachedClipMetadata
        {
            public string cacheKey;
            public string promptSignature;
            public string prompt;
            public string model;
            public string reason;
            public string sourceFilePath;
            public string cachedAtUtc;
        }

        private struct CachedClipRecord
        {
            public string cacheKey;
            public string promptSignature;
            public string clipPath;
            public string model;
        }

        private class BackendGenerationAttemptResult
        {
            public bool success;
            public string statusMessage = string.Empty;
            public string errorDetail = string.Empty;
            public string savedFileName = string.Empty;
            public byte[] audioBytes;
        }
    }
}
