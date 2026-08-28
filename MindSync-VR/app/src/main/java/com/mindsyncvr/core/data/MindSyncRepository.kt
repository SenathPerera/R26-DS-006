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
import com.mindsyncvr.core.voice.AudioPayload
import com.mindsyncvr.core.voice.CaptureParams
import com.mindsyncvr.core.voice.MockVoiceStressRepository
import com.mindsyncvr.core.voice.SessionPhase
import com.mindsyncvr.core.voice.VoiceError
import com.mindsyncvr.core.voice.VoiceStressRepository
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

    private fun voiceUserId() = state.value.user?.id ?: "mobile-demo"
    private fun voiceLanguage() = state.value.onboarding.preferredLanguage.lowercase()

    private fun resolveFirstName(): String {
        val full = (state.value.user?.name ?: state.value.onboarding.name).trim()
        return full.split(" ").firstOrNull()?.takeIf { it.isNotBlank() } ?: "there"
    }

    private fun q(bank: List<String>, index: Int, name: String) =
        bank[index % bank.size].replace("{name}", name)

    /** Start the flow at Layer 1 — the companion asks for a quiet room. */
    fun startVoiceCheckIn() {
        val sid = state.value.activeSession?.id ?: UUID.randomUUID().toString()
        val name = resolveFirstName()
        updateVoice {
            VoiceCheckInState(
                active = true, stage = VoiceStage.Environment, sessionId = sid, personName = name,
                conversationPre = listOf(CompanionTurn(false, ENV_INTRO.replace("{name}", name))),
                checkingAmbient = false, awaitingAmbient = true,
            )
        }
        scope.launch {
            voice.health()
                .onSuccess { h -> updateVoice { it.copy(backendHealthy = h.layers.fusion) } }
                .onFailure { updateVoice { it.copy(backendHealthy = false) } }
        }
    }

    /** Layer 1 — the room clip (recorded automatically while the person stays quiet). */
    fun submitAmbientClip(audio: AudioPayload?) {
        val sid = state.value.voiceCheckIn.sessionId ?: return
        if (audio == null) { updateVoice { it.copy(awaitingAmbient = true, captureToken = it.captureToken + 1) }; return }
        scope.launch {
            updateVoice { it.copy(checkingAmbient = true, awaitingAmbient = false, error = null) }
            voice.ambientCheck(audio)
                .onSuccess { res ->
                    if (res.ok) beginPreConversation()
                    else updateVoice {
                        it.copy(
                            checkingAmbient = false, ambientOk = false, awaitingAmbient = true,
                            captureToken = it.captureToken + 1,
                            conversationPre = it.conversationPre + CompanionTurn(false, ENV_NOISY.replace("{name}", it.personName)),
                        )
                    }
                }
                .onFailure { e ->
                    // Don't hard-block on an ambient hiccup — move into the conversation.
                    if (e.voiceError is VoiceError.BackendUnavailable || e.voiceError is VoiceError.NetworkUnavailable)
                        updateVoice { it.copy(checkingAmbient = false, error = (e.voiceError ?: VoiceError.Unknown(e.message)).message()) }
                    else beginPreConversation()
                }
        }
    }

    private fun beginPreConversation() = updateVoice {
        it.copy(
            stage = VoiceStage.PreConversation, checkingAmbient = false, ambientOk = true,
            awaitingAmbient = false, awaitingCapture = true, captureToken = it.captureToken + 1, followUpIndex = 0,
            conversationPre = it.conversationPre + CompanionTurn(false, q(PRE_QUESTIONS, 0, it.personName)),
        )
    }

    /** Layer 2 — a spoken clip captured automatically. If too little speech, the
     *  companion asks another question and keeps listening; otherwise it is scored. */
    fun submitVoiceCapture(phase: SessionPhase, audio: AudioPayload?, speechSec: Int) {
        val sid = state.value.voiceCheckIn.sessionId ?: return
        val bank = if (phase == SessionPhase.Pre) PRE_QUESTIONS else POST_QUESTIONS

        if (audio == null || speechSec < CaptureParams.MIN_SPEECH_SEC) {
            updateVoice {
                val next = it.followUpIndex + 1
                it.addTurn(phase, CompanionTurn(false, q(bank, next, it.personName)))
                    .copy(followUpIndex = next, awaitingCapture = true, captureToken = it.captureToken + 1)
            }
            return
        }

        scope.launch {
            updateVoice { it.copy(awaitingCapture = false, analyzing = true, error = null) }
            voice.analyzeVoice(
                sessionId = sid, phase = phase, audio = audio,
                userId = voiceUserId(), language = voiceLanguage(), pollB = false, log = false,
            ).onSuccess { va ->
                updateVoice {
                    if (phase == SessionPhase.Pre) it.copy(pre = va, analyzing = false)
                    else it.copy(post = va, analyzing = false)
                }
                // Score is now stored server-side -> companion reflects via its sensor note.
                sendCompanionMessageInternal(phase, if (phase == SessionPhase.Pre) SEED_PRE else SEED_POST, visible = false)
            }.onFailure { e ->
                when (e.voiceError) {
                    is VoiceError.AudioRejected -> updateVoice {
                        val next = it.followUpIndex + 1
                        it.addTurn(phase, CompanionTurn(false, RETRY_LINE.replace("{name}", it.personName)))
                            .copy(analyzing = false, followUpIndex = next, awaitingCapture = true, captureToken = it.captureToken + 1)
                    }
                    else -> updateVoice { it.copy(analyzing = false, error = (e.voiceError ?: VoiceError.Unknown(e.message)).message()) }
                }
            }
        }
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
        updateVoice {
            when (stage) {
                VoiceStage.PostConversation -> it.copy(
                    stage = stage, followUpIndex = 0, awaitingCapture = true, captureToken = it.captureToken + 1,
                    conversationPost = listOf(CompanionTurn(false, q(POST_QUESTIONS, 0, it.personName))),
                )
                else -> it.copy(stage = stage)
            }
        }
    }

    /** Layers 3+4+5 — the report, after both pre and post clips exist. */
    fun completeVoiceCheckIn() {
        val sid = state.value.voiceCheckIn.sessionId ?: return
        scope.launch {
            updateVoice { it.copy(stage = VoiceStage.Report, generatingReport = true, error = null) }
            voice.completeSession(sessionId = sid, userId = voiceUserId(), useMockHrv = true, language = voiceLanguage(), log = false)
                .onSuccess { report -> updateVoice { it.copy(report = report, generatingReport = false) } }
                .onFailure { e -> updateVoice { it.copy(generatingReport = false, error = (e.voiceError ?: VoiceError.Unknown(e.message)).message()) } }
        }
    }

    fun endVoiceCheckIn() = updateVoice { VoiceCheckInState(active = false) }

    private companion object {
        const val MAX_RECENT_PPG_SAMPLES = 1_500

        const val ENV_INTRO = "Hi {name}, it's really good to see you. Before we begin, I just need the room to be quiet for a moment — stay still and silent for a few seconds while I listen to the space around you."
        const val ENV_NOISY = "It's a little noisy where you are, {name}. Let's find a quieter spot, then stay silent for a moment and we'll try again."
        const val RETRY_LINE = "I couldn't quite catch that, {name}. Take your time — tell me a little more, in your own words."
        const val SEED_PRE = "I just spoke about how I'm arriving today."
        const val SEED_POST = "I just spoke about how I feel after the session."

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
    }
}
