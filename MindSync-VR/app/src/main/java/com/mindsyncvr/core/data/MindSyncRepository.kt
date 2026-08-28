package com.mindsyncvr.core.data

import com.mindsyncvr.core.bluetooth.BleController
import com.mindsyncvr.core.bluetooth.MockBleController
import com.mindsyncvr.core.bluetooth.PpgVitalsProcessor
import com.mindsyncvr.core.model.*
import com.mindsyncvr.core.network.ApiClient
import com.mindsyncvr.core.network.MockApiClient
import com.mindsyncvr.core.realtime.MockRealtimeSessionClient
import com.mindsyncvr.core.realtime.RealtimeSessionClient
import com.mindsyncvr.core.unity.MockUnityBridge
import com.mindsyncvr.core.unity.UnityBridge
import android.util.Log
import com.mindsyncvr.BuildConfig
import com.mindsyncvr.core.voice.AudioPayload
import com.mindsyncvr.core.voice.CaptureParams
import com.mindsyncvr.core.voice.MockVoiceStressRepository
import com.mindsyncvr.core.voice.SessionPhase
import com.mindsyncvr.core.voice.TurnPolicy
import com.mindsyncvr.core.voice.VoiceError
import com.mindsyncvr.core.voice.VoiceStressRepository
import com.mindsyncvr.core.voice.VoiceTurnResult
import com.mindsyncvr.core.voice.WavUtil
import com.mindsyncvr.core.voice.message
import com.mindsyncvr.core.voice.voiceError
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.Instant
import java.util.UUID

class MindSyncRepository(
    private val api: ApiClient = MockApiClient(),
    private val ble: BleController = MockBleController(),
    private val realtime: RealtimeSessionClient = MockRealtimeSessionClient(),
    val unityBridge: UnityBridge = MockUnityBridge(),
    // Component D (voice stress). Defaults to the mock so any caller/test builds
    // without a backend; MindSyncViewModel injects the real client for the app.
    private val voice: VoiceStressRepository = MockVoiceStressRepository()
) {
    private val ppgVitalsProcessor = PpgVitalsProcessor()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val mutableState = MutableStateFlow(
        AppState(
            questionnaireTemplates = MockData.questionnaires,
            sessions = MockData.sessions
        )
    )
    val state: StateFlow<AppState> = mutableState

    init {
        scope.launch {
            ble.state.collect { connectionState ->
                mutableState.update { it.copy(wearableState = connectionState) }
            }
        }
        scope.launch {
            ble.logs.collect { logs ->
                mutableState.update {
                    it.copy(bleIngestion = it.bleIngestion.copy(logs = logs))
                }
            }
        }
        scope.launch {
            ble.errors.collect { error ->
                mutableState.update {
                    it.copy(bleIngestion = it.bleIngestion.copy(lastError = error))
                }
            }
        }
        scope.launch {
            ble.telemetry.collect { telemetry ->
                mutableState.update { current ->
                    current.copy(
                        selectedWearable = current.selectedWearable?.copy(
                            battery = telemetry.batteryPercent ?: current.selectedWearable.battery,
                            lastSync = "Live"
                        ),
                        bleIngestion = current.bleIngestion.copy(
                            isStreaming = true,
                            latestTelemetry = telemetry,
                            latestSample = telemetry.ir?.let {
                                RawPpgSample(
                                    timestampMs = telemetry.timestampMs ?: System.currentTimeMillis(),
                                    irValue = it
                                )
                            } ?: current.bleIngestion.latestSample,
                            telemetryCount = current.bleIngestion.telemetryCount + 1,
                            sampleCount = current.bleIngestion.sampleCount + if (telemetry.ir != null) 1 else 0,
                            lastPacketSampleCount = 1,
                            lastError = null
                        )
                    )
                }
            }
        }
        scope.launch {
            ble.ppgSamples.collect { samples ->
                if (samples.isEmpty()) return@collect
                mutableState.update { current ->
                    val recent = (current.recentPpgSamples + samples).takeLast(MAX_RECENT_PPG_SAMPLES)
                    val vitals = ppgVitalsProcessor.process(recent)
                    current.copy(
                        recentPpgSamples = recent,
                        ppgVitals = vitals,
                        selectedWearable = current.selectedWearable?.copy(
                            signalQuality = vitals.signalQuality,
                            confidence = vitals.confidence
                        ),
                        bleIngestion = current.bleIngestion.copy(
                            isStreaming = true,
                            latestSample = samples.last(),
                            sampleCount = current.bleIngestion.sampleCount + samples.size,
                            lastPacketSampleCount = samples.size,
                            lastError = null
                        )
                    )
                }
            }
        }
    }

    suspend fun login(email: String, password: String) {
        api.post("/auth/login", mapOf("email" to email, "password" to password))
        mutableState.update { it.copy(user = MockData.demoUser.copy(email = email)) }
    }

    suspend fun register(name: String, email: String, password: String) {
        api.post("/auth/register", mapOf("name" to name, "email" to email, "password" to password))
        mutableState.update { it.copy(user = MockData.demoUser.copy(name = name, email = email)) }
    }

    fun updateOnboarding(profile: OnboardingProfile) {
        mutableState.update { it.copy(onboarding = profile) }
    }

    fun completeOnboarding() {
        mutableState.update { current ->
            current.copy(user = current.user?.copy(onboardingComplete = true) ?: MockData.demoUser.copy(onboardingComplete = true))
        }
    }

    suspend fun scanWearables() {
        runCatching { ble.scan() }
            .onSuccess { devices ->
                mutableState.update {
                    it.copy(
                        wearableDevices = devices,
                        wearableState = if (devices.isEmpty()) ConnectionState.Error else ConnectionState.Idle,
                        bleIngestion = it.bleIngestion.copy(
                            lastError = if (devices.isEmpty()) "WearableHealthMonitor not found" else null
                        )
                    )
                }
            }
            .onFailure { error ->
                mutableState.update {
                    it.copy(
                        wearableState = ConnectionState.Error,
                        bleIngestion = it.bleIngestion.copy(lastError = error.message)
                    )
                }
            }
    }

    suspend fun connectWearable(id: String) {
        runCatching { ble.connect(id) }
            .onSuccess { device ->
                mutableState.update {
                    it.copy(
                        selectedWearable = device,
                        wearableState = ConnectionState.Connected,
                        bleIngestion = it.bleIngestion.copy(lastError = null)
                    )
                }
            }
            .onFailure { error ->
                mutableState.update {
                    it.copy(
                        wearableState = ConnectionState.Error,
                        bleIngestion = it.bleIngestion.copy(lastError = error.message)
                    )
                }
            }
    }

    suspend fun disconnectWearable() {
        ble.disconnect()
        mutableState.update {
            it.copy(
                wearableState = ConnectionState.Disconnected,
                bleIngestion = it.bleIngestion.copy(isStreaming = false)
            )
        }
    }

    suspend fun calibrateWearable(): String {
        val id = state.value.selectedWearable?.id ?: return "Connect a wearable before calibration."
        return ble.calibrate(id)
    }

    suspend fun pairVr() {
        val device = VrDevice("vr-demo-headset", "MindSync VR Lab Headset", "MSVR-4281", VrStatus.Ready, "backend_bridge")
        mutableState.update { it.copy(vrDevice = device, vrStatus = VrStatus.Ready) }
    }

    fun createSession(): MeditationSession {
        val session = MeditationSession(
            // A real UUID: this is the correlation key Component D threads across
            // pre/post /infer and /full-session for one Cognify session.
            id = UUID.randomUUID().toString(),
            title = "Adaptive Calm Session",
            durationMinutes = state.value.onboarding.preferredDuration,
            environment = state.value.onboarding.environmentPreferences.firstOrNull() ?: "Ocean dusk",
            audioProfile = state.value.onboarding.audioPreferences.joinToString().ifBlank { "Warm pads + breath pacing" },
            completionRate = 0,
            moodBefore = state.value.onboarding.baselineMood,
            moodAfter = 0,
            validationComplete = false
        )
        unityBridge.attachSession(session.id, state.value.onboarding)
        mutableState.update { it.copy(activeSession = session, vrStatus = VrStatus.Waiting) }
        return session
    }

    fun startLiveSession(sessionId: String) {
        scope.launch {
            realtime.subscribe(sessionId).collect { live ->
                unityBridge.sendLiveState(live)
                mutableState.update { it.copy(liveSession = live, vrStatus = VrStatus.Active) }
            }
        }
    }

    suspend fun submitQuestionnaire(templateId: String, sessionId: String?, answers: Map<String, String>) {
        val response = QuestionnaireResponse(
            id = "response-${System.currentTimeMillis()}",
            templateId = templateId,
            sessionId = sessionId,
            userId = state.value.user?.id ?: "anonymous",
            submittedAt = Instant.now().toString(),
            synced = false,
            answers = answers
        )
        api.post("/questionnaires/submit", response)
        mutableState.update {
            it.copy(
                questionnaireResponses = listOf(response) + it.questionnaireResponses,
                pendingValidationCount = (it.pendingValidationCount - 1).coerceAtLeast(0)
            )
        }
    }

    // ---------------------------------------------------------------- Component D voice flow
    // Companion-led: Layer 1 environment -> Layer 2 pre conversation (auto-recorded)
    // -> VR hand-off -> Layer 2 post -> Layers 3+4+5 report. Recording is automatic;
    // the person is never shown a record button.

    private fun updateVoice(f: (VoiceCheckInState) -> VoiceCheckInState) =
        mutableState.update { it.copy(voiceCheckIn = f(it.voiceCheckIn)) }

    // Raw PCM of every speech-bearing turn in the CURRENT phase, concatenated into
    // ONE clip for the final scoring call (BUG-3/WP4). Reset when a phase's capture
    // begins so pre and post never bleed into each other.
    private var pcmChunks = mutableListOf<ByteArray>()
    private fun resetCapture() { pcmChunks = mutableListOf() }

    private fun escalationBank(phase: SessionPhase) =
        if (phase == SessionPhase.Pre) PRE_ESCALATION else POST_ESCALATION

    private fun voiceUserId() = state.value.user?.id ?: "mobile-demo"
    private fun voiceLanguage() = state.value.voiceCheckIn.language.lowercase()

    /** The person's first name for the companion to use — never the "Ari" demo
     *  placeholder (BUG-7). Returns "" when we don't reliably know it, so the Intro
     *  step asks. `register()` sets a real name; `login()` leaves the placeholder. */
    private fun resolveFirstName(): String {
        val raw = (state.value.user?.name ?: "").trim()
        val reliable = if (raw.isNotBlank() && raw != MockData.demoUser.name) raw
        else state.value.onboarding.name.trim()
        return reliable.split(" ").firstOrNull()?.takeIf { it.isNotBlank() } ?: ""
    }

    private fun q(bank: List<String>, index: Int, name: String) =
        bank[index % bank.size].replace("{name}", name)

    /** Start MY flow. The Pro spec asked to add createSession() at the HomeScreen
     *  hook, but that screen has no `actions` handle, so wiring it there would force
     *  editing the (teammate-owned) navigation file too. Instead I create the linked
     *  session HERE — same outcome, zero edits outside my folders. Opens on the Intro
     *  step (name + language) so we never greet a stranger as "Ari". */
    fun startVoiceCheckIn() {
        val session = state.value.activeSession ?: createSession()
        val prefName = resolveFirstName()
        val prefLang = state.value.onboarding.preferredLanguage.lowercase().ifBlank { "english" }
        updateVoice {
            VoiceCheckInState(
                active = true, stage = VoiceStage.Intro, sessionId = session.id,
                personName = prefName, language = if (prefLang == "sinhala") "sinhala" else "english",
                // In debug builds default to simulated HRV so Layer 4 cross-validation
                // is visible without Component B connected (matches the web demo). The
                // on-screen toggle can turn it off; release builds default to real B.
                debugForceMockHrv = BuildConfig.DEBUG,
            )
        }
        scope.launch {
            voice.health()
                .onSuccess { h -> updateVoice { it.copy(backendHealthy = h.layers.fusion) } }
                .onFailure { updateVoice { it.copy(backendHealthy = false) } }
        }
    }

    /** Intro → Layer 1: record the chosen name + language, greet by name, and open
     *  the environment check. Called from the Intro step (my own flow). */
    fun beginEnvironmentCheck(name: String, language: String) {
        val clean = name.trim().split(" ").firstOrNull()?.takeIf { it.isNotBlank() } ?: "there"
        val lang = if (language.lowercase() == "sinhala") "sinhala" else "english"
        updateVoice {
            it.copy(
                stage = VoiceStage.Environment, personName = clean, language = lang,
                checkingAmbient = false, awaitingAmbient = true, ambientAttempts = 0,
                conversationPre = listOf(CompanionTurn(false, ENV_INTRO.replace("{name}", clean))),
            )
        }
    }

    /** Layer 1 — the room clip (recorded automatically while the person stays quiet).
     *  The gate is CLOSED until a genuine `ok:true` from /ambient-check (BUG-4): no
     *  error path ever opens it, and there is no skip. */
    fun submitAmbientClip(audio: AudioPayload?) {
        val sid = state.value.voiceCheckIn.sessionId ?: return
        if (audio == null) { updateVoice { it.copy(awaitingAmbient = true, captureToken = it.captureToken + 1) }; return }
        scope.launch {
            updateVoice { it.copy(checkingAmbient = true, awaitingAmbient = false, error = null) }
            voice.ambientCheck(audio)
                .onSuccess { res ->
                    // Carry the measured room noise floor forward as an ADAPTIVE speech
                    // threshold (WP2) — a soft speaker in a quiet room and a loud one in
                    // a noisy room are both read correctly. Track the best score so a
                    // struggling person can tell whether moving actually helped.
                    val floor = res.metrics?.noiseFloorRms
                    val threshold = floor?.let {
                        (it * com.mindsyncvr.core.voice.CaptureParams.THRESHOLD_FLOOR_MULT)
                            .coerceIn(com.mindsyncvr.core.voice.CaptureParams.THRESHOLD_MIN,
                                      com.mindsyncvr.core.voice.CaptureParams.THRESHOLD_MAX)
                    }
                    updateVoice {
                        it.copy(
                            ambient = res,
                            ambientBestScore = maxOf(it.ambientBestScore ?: 0, res.score),
                            speechThresholdRms = threshold ?: it.speechThresholdRms,
                        )
                    }
                    // On a pass, stop and wait for the person to tap Continue so they
                    // actually SEE the score; on a fail, speak a noise-specific suggestion.
                    if (res.ok) updateVoice { it.copy(checkingAmbient = false, ambientOk = true, awaitingAmbient = false) }
                    else failAmbient(noiseSuggestion(res.noiseType, res.reasons))
                }
                .onFailure { e ->
                    // Any failure keeps the gate CLOSED and re-arms the listen. A 400
                    // (no audio) is a "say-again"; a genuine outage shows its message.
                    val ve = e.voiceError ?: VoiceError.Unknown(e.message)
                    failAmbient(ve.message(), showAsError = ve is VoiceError.BackendUnavailable ||
                        ve is VoiceError.NetworkUnavailable || ve is VoiceError.Timeout)
                }
        }
    }

    /** Re-arm the room check after a failure, speaking a specific, human suggestion.
     *  After a few tries, add a "different room" tip — but never open the gate. */
    private fun failAmbient(line: String, showAsError: Boolean = false) = updateVoice {
        val attempts = it.ambientAttempts + 1
        val tip = if (attempts >= 3) " If it keeps happening, moving to a different room usually helps." else ""
        it.copy(
            checkingAmbient = false, ambientOk = false, awaitingAmbient = true,
            ambientAttempts = attempts, captureToken = it.captureToken + 1,
            error = if (showAsError) line else null,
            conversationPre = it.conversationPre + CompanionTurn(false, line.replace("{name}", it.personName) + tip),
        )
    }

    /** WP1 — a spoken suggestion keyed to the CLASSIFIED noise type, so the
     *  companion says something specific ("move away from the fan") instead of a
     *  vague "it's noisy". Falls back to the reason-code line for older servers. */
    private fun noiseSuggestion(noiseType: String, reasons: List<String>): String = when (noiseType) {
        "hum" -> "There's a steady hum, {name} — could you move away from the fan or air conditioning, or switch it off for a minute?"
        "broadband" -> "There's some background noise, {name} — could you close the window, or move away from the road?"
        "voices" -> "I can hear someone talking nearby — somewhere more private would help."
        "intermittent" -> "There's some movement around you — let's wait for it to settle, then try again."
        "hiss" -> "There's a faint electrical hiss — moving away from the desk or charger might help."
        else -> ambientReasonLine(reasons)
    }

    /** Turn Layer-1's raw reason codes into a spoken, human suggestion (WP5). */
    private fun ambientReasonLine(reasons: List<String>): String {
        val code = reasons.firstOrNull()?.substringBefore(":")?.trim().orEmpty()
        return when {
            code.startsWith("voice_detected") -> "I can hear someone talking nearby — let's wait for a quiet moment, then stay silent while I listen again."
            code.startsWith("too_noisy") || code.startsWith("too_loud") -> "There's a lot of background sound around you — could you move somewhere quieter, then stay silent for a moment?"
            code.startsWith("clipping") -> "Something's very close to the microphone — let's give it a little space, then try again in silence."
            code.startsWith("too_short") || code.startsWith("too_quiet") -> "I didn't get a long enough listen — let's try that again, staying still and silent."
            else -> ENV_NOISY
        }
    }

    /** Layer 1 → Layer 2, after the person has seen the room score and tapped Continue. */
    fun continueFromEnvironment() { if (state.value.voiceCheckIn.ambientOk == true) beginPreConversation() }

    private fun beginPreConversation() {
        resetCapture()
        updateVoice {
            it.copy(
                stage = VoiceStage.PreConversation, checkingAmbient = false, ambientOk = true,
                awaitingAmbient = false, awaitingCapture = true, captureToken = it.captureToken + 1,
                escalationIndex = 0, turnCount = 0, capturedSpeechSec = 0, lowConfidenceCapture = false,
                conversationPre = it.conversationPre + CompanionTurn(false, q(PRE_QUESTIONS, 0, it.personName)),
            )
        }
    }

    /**
     * Layer 2 — one automatic spoken turn. Its raw PCM accumulates across turns;
     * each turn goes to `/companion/voice-turn` (is_final=false) so the companion
     * reflects on what was actually said (BUG-2) and the person sees their own
     * transcribed words. When the CUMULATIVE speech budget is met — or the 5-turn
     * escape fires — the concatenated clip is scored once (is_final=true, BUG-3).
     */
    fun submitVoiceCapture(phase: SessionPhase, audio: AudioPayload?, pcm: ByteArray?, speechSec: Int) {
        val sid = state.value.voiceCheckIn.sessionId ?: return
        if (state.value.voiceCheckIn.crisis) return   // crisis is terminal — never re-arm

        val hadSpeech = audio != null && pcm != null && speechSec > 0
        if (hadSpeech) pcmChunks.add(pcm!!)

        val vc = state.value.voiceCheckIn
        val cumulative = vc.capturedSpeechSec + if (hadSpeech) speechSec else 0
        val turnCount = vc.turnCount + 1
        val finalize = TurnPolicy.shouldFinalize(cumulative, turnCount) && pcmChunks.isNotEmpty()

        scope.launch {
            if (finalize) finalizeCapture(sid, phase, cumulative, turnCount)
            else continueConversation(sid, phase, audio, cumulative, turnCount)
        }
    }

    /** Not enough yet: transcribe + reply for this turn (or escalate if minimal),
     *  then re-arm listening. Never dead-ends; the LLM being down falls back to the
     *  escalation bank. */
    private suspend fun continueConversation(
        sid: String, phase: SessionPhase, audio: AudioPayload?, cumulative: Int, turnCount: Int,
    ) {
        // Ceiling reached but not one word captured: honest stop, never an endless loop.
        if (turnCount >= CaptureParams.MAX_TURNS && pcmChunks.isEmpty()) {
            Log.i(VOICE_TAG, "no speech after $turnCount turns; stopping this step")
            updateVoice {
                it.copy(companionThinking = false, awaitingCapture = false,
                    capturedSpeechSec = cumulative, turnCount = turnCount,
                    error = "I wasn't able to hear your voice. When you're ready, we can try this step again.")
            }
            return
        }

        updateVoice { it.copy(companionThinking = true, capturedSpeechSec = cumulative, turnCount = turnCount) }

        // A silent turn (no audio at all) -> straight to the escalation ladder.
        if (audio == null) { escalate(phase); return }

        val result = voice.voiceTurn(
            sid, phase, audio, isFinal = false,
            userId = voiceUserId(), language = voiceLanguage(), pollB = false, log = false,
        ).getOrNull()

        // Backend / Ollama unreachable -> keep the flow alive on the local bank.
        if (result == null) { escalate(phase); return }
        if (result.crisis) { enterCrisis(phase, result.reply); return }

        updateVoice {
            var s = it.copy(companionThinking = false)
            // Note: we deliberately DON'T echo the person's transcript into the chat —
            // it clutters the companion conversation. It's still used for the reply and
            // is saved server-side (log=true) for the study record.
            val minimal = result.transcript.isBlank()
            val bank = escalationBank(phase)
            val (nextLine, nextEsc) = if (minimal) {
                val idx = s.escalationIndex.coerceIn(0, bank.size - 1)
                bank[idx].replace("{name}", s.personName) to (s.escalationIndex + 1).coerceAtMost(bank.size - 1)
            } else {
                val idx = s.escalationIndex.coerceIn(0, bank.size - 1)
                val line = result.reply.ifBlank { bank[idx].replace("{name}", s.personName) }
                line to s.escalationIndex
            }
            s.addTurn(phase, CompanionTurn(false, nextLine))
                .copy(awaitingCapture = true, captureToken = s.captureToken + 1, escalationIndex = nextEsc)
        }
    }

    /** Budget met (or escape hatch): score the ONE concatenated clip and store the
     *  result, then show the companion's closing reflection. */
    private suspend fun finalizeCapture(sid: String, phase: SessionPhase, cumulative: Int, turnCount: Int) {
        val escape = TurnPolicy.isEscapeHatch(cumulative, turnCount)
        if (escape) Log.i(VOICE_TAG, "5-turn escape: scoring ${cumulative}s of speech (low-confidence)")
        updateVoice {
            it.copy(awaitingCapture = false, companionThinking = false, analyzing = true, error = null,
                capturedSpeechSec = cumulative, turnCount = turnCount, lowConfidenceCapture = escape)
        }

        val payload = AudioPayload(WavUtil.wrapWav(WavUtil.concatPcm(pcmChunks)), "checkin.wav", "audio/wav")
        // pollB=true: Component D polls Component B's live HRV AT THE MOMENT the
        // person speaks, so the body reading is time-aligned with the voice (WP6).
        // log=true: the server saves this exact clip + its score to session_logs/,
        // so the live recording that reached the pipeline can be inspected as proof.
        val result = voice.voiceTurn(
            sid, phase, payload, isFinal = true,
            userId = voiceUserId(), language = voiceLanguage(), pollB = true, log = true,
        ).getOrNull()

        if (result == null) {
            updateVoice { it.copy(analyzing = false, error = VoiceError.NetworkUnavailable.message()) }
            return
        }
        if (result.crisis) { enterCrisis(phase, result.reply); return }

        val analysis = result.analysis
        if (analysis == null) {
            // The whole concatenated clip was rejected by Layer 1 (rare — lots of
            // speech). Re-ask once, but don't loop forever if it keeps failing.
            if (turnCount >= CaptureParams.MAX_TURNS + 2) {
                updateVoice { it.copy(analyzing = false, error =
                    "I couldn't get a clear enough recording. Let's try this step again when you're ready.") }
            } else {
                updateVoice {
                    it.addTurn(phase, CompanionTurn(false, RETRY_LINE.replace("{name}", it.personName)))
                        .copy(analyzing = false, awaitingCapture = true, captureToken = it.captureToken + 1)
                }
            }
            return
        }

        updateVoice {
            var s = it.copy(analyzing = false)
            s = if (phase == SessionPhase.Pre) s.copy(pre = analysis) else s.copy(post = analysis)
            if (result.reply.isNotBlank()) s = s.addTurn(phase, CompanionTurn(false, result.reply))
            s
        }
    }

    /** Draw a quiet speaker out with the next ladder rung, then re-arm listening. */
    private fun escalate(phase: SessionPhase) = updateVoice {
        val bank = escalationBank(phase)
        val idx = it.escalationIndex.coerceIn(0, bank.size - 1)
        it.addTurn(phase, CompanionTurn(false, bank[idx].replace("{name}", it.personName)))
            .copy(companionThinking = false, awaitingCapture = true, captureToken = it.captureToken + 1,
                escalationIndex = (it.escalationIndex + 1).coerceAtMost(bank.size - 1))
    }

    /** Crisis is a distinct, terminal state: show the calm reply, stop scoring. */
    private fun enterCrisis(phase: SessionPhase, reply: String) = updateVoice {
        it.addTurn(phase, CompanionTurn(false, reply))
            .copy(crisis = true, crisisReply = reply, companionThinking = false, analyzing = false,
                awaitingCapture = false, awaitingAmbient = false)
    }

    /** Optional: a typed line to the companion. */
    fun sendCompanionMessage(phase: SessionPhase, text: String) {
        if (text.isBlank()) return
        scope.launch { sendCompanionMessageInternal(phase, text, visible = true) }
    }

    private suspend fun sendCompanionMessageInternal(phase: SessionPhase, text: String, visible: Boolean) {
        val sid = state.value.voiceCheckIn.sessionId ?: return
        if (visible) updateVoice { it.addTurn(phase, CompanionTurn(true, text)) }
        updateVoice { it.copy(companionThinking = true) }
        voice.companionMessage(sid, text, phase)
            .onSuccess { reply -> updateVoice { it.addTurn(phase, CompanionTurn(false, reply.reply)).copy(companionThinking = false) } }
            .onFailure { updateVoice { it.copy(companionThinking = false) } }   // companion is optional; never blocks the flow
    }

    private fun VoiceCheckInState.addTurn(phase: SessionPhase, turn: CompanionTurn): VoiceCheckInState =
        if (phase == SessionPhase.Pre) copy(conversationPre = conversationPre + turn)
        else copy(conversationPost = conversationPost + turn)

    /** Enter the VR hand-off, or open the post conversation (arming listening). */
    fun advanceVoiceStage(stage: VoiceStage) {
        if (stage == VoiceStage.PostConversation) resetCapture()
        updateVoice {
            when (stage) {
                VoiceStage.PostConversation -> it.copy(
                    stage = stage, escalationIndex = 0, turnCount = 0, capturedSpeechSec = 0,
                    lowConfidenceCapture = false, awaitingCapture = true, captureToken = it.captureToken + 1,
                    conversationPost = listOf(CompanionTurn(false, q(POST_QUESTIONS, 0, it.personName))),
                )
                else -> it.copy(stage = stage)
            }
        }
    }

    /** Debug-only: toggle simulated HRV for demos when Component B isn't connected. */
    fun setDebugMockHrv(enabled: Boolean) = updateVoice { it.copy(debugForceMockHrv = enabled) }

    /** Layers 3+4+5 — the report, after both pre and post clips exist. Uses REAL
     *  Component B unless the debug demo toggle forces mock HRV (BUG-6). When B
     *  isn't connected, crossmodal comes back null and the report says so honestly
     *  rather than fabricating agreement. */
    fun completeVoiceCheckIn() {
        val sid = state.value.voiceCheckIn.sessionId ?: return
        val useMock = state.value.voiceCheckIn.debugForceMockHrv
        scope.launch {
            updateVoice { it.copy(stage = VoiceStage.Report, generatingReport = true, error = null) }
            // WP8 Tier 2: persist the completed session (clips + scores + language)
            // as labelled study data. On for debug builds; the server writes
            // sessions.jsonl + the SQLite record either way.
            voice.completeSession(sessionId = sid, userId = voiceUserId(), useMockHrv = useMock, language = voiceLanguage(), log = BuildConfig.DEBUG)
                .onSuccess { report -> updateVoice { it.copy(report = report, generatingReport = false) } }
                .onFailure { e -> updateVoice { it.copy(generatingReport = false, error = (e.voiceError ?: VoiceError.Unknown(e.message)).message()) } }
        }
    }

    fun endVoiceCheckIn() = updateVoice { VoiceCheckInState(active = false) }

    private companion object {
        const val MAX_RECENT_PPG_SAMPLES = 1_500
        const val VOICE_TAG = "MindSyncVoice"

        const val ENV_INTRO = "Hi {name}, it's really good to see you. Before we begin, I just need the room to be quiet for a moment — stay still and silent for a few seconds while I listen to the space around you."
        const val ENV_NOISY = "It's a little noisy where you are, {name}. Let's find a quieter spot, then stay silent for a moment and we'll try again."
        const val RETRY_LINE = "I couldn't quite catch that, {name}. Take your time — tell me a little more, in your own words."

        // Opening lines. Also the FALLBACK bank when the backend / Ollama is
        // unreachable (the flow must never die because the LLM is down — WP3).
        val PRE_QUESTIONS = listOf(
            "So {name}, take a slow breath… how has your day really been?",
            "Tell me a little about what's been on your mind today, {name}.",
            "Is there something in particular that's been weighing on you?",
            "Take your time, {name} — what happened today that made it feel this way?",
        )
        val POST_QUESTIONS = listOf(
            "Welcome back, {name}. Sit for a moment… how are you feeling now?",
            "What feels different for you now, {name}?",
            "Notice your body for a second — what do you feel right now, {name}?",
            "Take your time — what's still with you after the session, {name}?",
        )

        // Escalation ladder for a quiet / minimal speaker — four rungs, increasing
        // in concreteness (WP3). Never guilt or scold; never repeated within a phase.
        val PRE_ESCALATION = listOf(
            "How has your day really been, {name}?",
            "Walk me through today, {name} — what did you actually do?",
            "What's been sitting heaviest on you today?",
            "I just need to hear a little more of your voice for this step, {name} — even telling me what you ate today is enough.",
        )
        val POST_ESCALATION = listOf(
            "How are you feeling now, {name}?",
            "Take a moment — what's different in your body compared to before?",
            "What's still sitting with you after the session?",
            "I just need a little more of your voice for this step, {name} — even a few words about how the room felt is enough.",
        )
    }
}
