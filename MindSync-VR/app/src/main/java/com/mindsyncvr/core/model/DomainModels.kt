package com.mindsyncvr.core.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

enum class UserRole { Participant, Clinician, Researcher }
enum class ConnectionState { Idle, Scanning, Pairing, Connected, Disconnected, Error }
enum class VrStatus { NotPaired, Pairing, Ready, Waiting, Active, Disconnected }
enum class StressBand { Restorative, Balanced, Elevated, High }
enum class SessionStatus { Ready, Active, Paused, Ending, Complete }
enum class QuestionType { SingleChoice, MultipleChoice, Likert, Text, Numeric, Slider, VoiceNote }

@Serializable
data class RawPpgSample(
    val timestampMs: Long,
    val irValue: Long
)

@Serializable
data class WearableTelemetry(
    @SerialName("t")
    val timestampMs: Long? = null,
    val ir: Long? = null,
    val red: Long? = null,
    @SerialName("hr")
    val heartRateBpm: Double? = null,
    @SerialName("rr")
    val rrIntervalMs: Long? = null,
    val spo2: Double? = null,
    @SerialName("nAvg")
    val noiseAverage: Long? = null,
    @SerialName("nPeak")
    val noisePeak: Long? = null,
    @SerialName("temp")
    val temperatureC: Double? = null,
    @SerialName("bat")
    val batteryPercent: Int? = null,
    @SerialName("flags")
    val statusFlags: Int = 0
)

@Serializable
data class BleIngestionStatus(
    val isStreaming: Boolean = false,
    val latestSample: RawPpgSample? = null,
    val latestTelemetry: WearableTelemetry? = null,
    val sampleCount: Long = 0,
    val telemetryCount: Long = 0,
    val lastPacketSampleCount: Int = 0,
    val lastError: String? = null,
    val logs: List<String> = emptyList()
)

@Serializable
data class PpgVitals(
    val bpm: Int? = null,
    val signalQuality: Int = 0,
    val confidence: Int = 0,
    val calmScore: Int = 0,
    val sampleRateHz: Double? = null,
    val peakCount: Int = 0,
    val windowSeconds: Double = 0.0,
    val status: String = "Waiting for stable PPG window"
)

@Serializable
data class UserProfile(
    val id: String,
    val email: String,
    val name: String,
    val role: UserRole = UserRole.Participant,
    val onboardingComplete: Boolean = false,
    val preferredLanguage: String = "English"
)

@Serializable
data class WearableDevice(
    val id: String,
    val name: String,
    val rssi: Int,
    val battery: Int,
    val firmware: String,
    val lastSync: String,
    val signalQuality: Int,
    val confidence: Int
)

@Serializable
data class VrDevice(
    val id: String,
    val name: String,
    val pairingCode: String,
    val status: VrStatus,
    val transport: String
)

@Serializable
data class ResearchComponentState(
    val signalConfidence: Int,
    val sensorQuality: String,
    val stressLevel: Int,
    val stressBand: StressBand,
    val stressSummary: String,
    val vrAdaptationState: String,
    val environmentProfile: String,
    val personalizationStatus: String,
    val validationPending: Boolean,
    val validationCompletion: String,
    val audioPersonalizationActive: Boolean,
    val soundAdaptationLevel: Int,
    val ambientBlendingState: String,
    val therapeuticAudioMode: String
)

@Serializable
data class MeditationSession(
    val id: String,
    val title: String,
    val durationMinutes: Int,
    val environment: String,
    val audioProfile: String,
    val completionRate: Int,
    val moodBefore: Int,
    val moodAfter: Int,
    val validationComplete: Boolean,
    val notes: String = ""
)

@Serializable
data class LiveSessionState(
    val sessionId: String,
    val elapsedSeconds: Int,
    val status: SessionStatus,
    val wearableConnected: Boolean,
    val vrConnected: Boolean,
    val research: ResearchComponentState
)

@Serializable
data class OnboardingProfile(
    val name: String = "",
    val ageRange: String = "",
    val gender: String = "Prefer not to say",
    val meditationExperience: String = "",
    val preferredLanguage: String = "English",
    val preferredDuration: Int = 15,
    val goals: List<String> = emptyList(),
    val meditationStyle: String = "Guided",
    val audioPreferences: List<String> = emptyList(),
    val environmentPreferences: List<String> = emptyList(),
    val sensitivities: List<String> = emptyList(),
    val baselineMood: Int = 6,
    val consentAccepted: Boolean = false,
    val researchConsent: Boolean = false
)

@Serializable
data class BranchRule(
    val whenQuestionId: String,
    val equals: String? = null,
    val includes: String? = null
)

@Serializable
data class QuestionnaireQuestion(
    val id: String,
    val prompt: String,
    val type: QuestionType,
    val required: Boolean = false,
    val helperText: String = "",
    val options: List<String> = emptyList(),
    val min: Int = 1,
    val max: Int = 7,
    val branch: BranchRule? = null
)

@Serializable
data class QuestionnaireTemplate(
    val id: String,
    val title: String,
    val description: String,
    val component: String,
    val version: String,
    val questions: List<QuestionnaireQuestion>
)

@Serializable
data class QuestionnaireResponse(
    val id: String,
    val templateId: String,
    val sessionId: String?,
    val userId: String,
    val submittedAt: String,
    val synced: Boolean,
    val exportShapeVersion: String = "component-d-v1",
    val answers: Map<String, String>
)

data class AppState(
    val user: UserProfile? = null,
    val onboarding: OnboardingProfile = OnboardingProfile(),
    val wearableDevices: List<WearableDevice> = emptyList(),
    val selectedWearable: WearableDevice? = null,
    val wearableState: ConnectionState = ConnectionState.Idle,
    val bleIngestion: BleIngestionStatus = BleIngestionStatus(),
    val ppgVitals: PpgVitals = PpgVitals(),
    val recentPpgSamples: List<RawPpgSample> = emptyList(),
    val vrDevice: VrDevice? = null,
    val vrStatus: VrStatus = VrStatus.NotPaired,
    val activeSession: MeditationSession? = null,
    val liveSession: LiveSessionState? = null,
    val sessions: List<MeditationSession> = emptyList(),
    val questionnaireTemplates: List<QuestionnaireTemplate> = emptyList(),
    val questionnaireResponses: List<QuestionnaireResponse> = emptyList(),
    val pendingValidationCount: Int = 1
)
