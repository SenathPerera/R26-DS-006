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
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.Instant

class MindSyncRepository(
    private val api: ApiClient = MockApiClient(),
    private val ble: BleController = MockBleController(),
    private val realtime: RealtimeSessionClient = MockRealtimeSessionClient(),
    val unityBridge: UnityBridge = MockUnityBridge()
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
            id = "session-${System.currentTimeMillis()}",
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

    private companion object {
        const val MAX_RECENT_PPG_SAMPLES = 1_500
    }
}
