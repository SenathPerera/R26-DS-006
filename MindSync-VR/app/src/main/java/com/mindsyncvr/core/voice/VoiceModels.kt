package com.mindsyncvr.core.voice

/**
 * Domain models for Component D — the app's own vocabulary, mapped from the wire
 * DTOs so Retrofit/kotlinx types never leak past the repository. Optional backend
 * sections stay nullable and honest: a null [SessionReport.crossmodal] or
 * [SessionReport.anomaly] means "that component/model wasn't available", NOT zero.
 */

/** Pre- or post-session voice check-in. `wire` is the value Component D expects. */
enum class SessionPhase(val wire: String) { Pre("pre"), Post("post") }

/** Automatic-capture policy, shared by the recorder (when to stop) and the flow
 *  (whether enough was said). Component D wants natural, ~30s conversational speech. */
object CaptureParams {
    const val MIN_SPEECH_SEC = 6      // UI hint threshold ("keep going") only
    const val MAX_SEC = 30            // hard cap on ONE listening window
    // A turn ends only after a LONG pause following real speech, so the companion
    // never cuts the person off mid-thought (their #1 complaint). 3.5s of silence
    // clearly means "done talking"; natural mid-sentence pauses are ~1s.
    const val SILENCE_TAIL_SEC = 3.5
    const val AMBIENT_SEC = 8         // Layer-1 room sample (person stays silent) — a deep listen

    // --- turn-taking: let the person FINISH before the companion responds ---
    // NEVER end a turn (on the speech path) before this many seconds of wall clock,
    // whatever the VAD thinks — the hard floor that kills the 4-second cutoff (WP2).
    const val MIN_LISTEN_SEC = 12
    // A turn only ends after the person has spoken at least this much AND then gone
    // quiet for SILENCE_TAIL_SEC — so a natural mid-thought pause never cuts them off.
    const val TURN_END_SPEECH_SEC = 4

    // Adaptive speech threshold = measured room noise floor × this, clamped. A fixed
    // threshold is wrong both ways (too high for a soft speaker in a quiet room, too
    // low in a noisy one); anchoring to the floor fixes both (WP2).
    const val THRESHOLD_FLOOR_MULT = 4.0
    const val THRESHOLD_MIN = 0.006
    const val THRESHOLD_MAX = 0.030
    // If the person says nothing at all, end the turn here so the companion can
    // gently re-ask instead of holding the mic open for the full MAX_SEC.
    const val NO_SPEECH_TIMEOUT_SEC = 10
    // Enough CUMULATIVE voiced speech (across turns) to score. Tune on real hardware.
    const val TARGET_SPEECH_SEC = 10
    // The companion asks between MIN and MAX questions: at least two so the reading
    // rests on more than one answer, at most three so it never drags (per spec).
    const val MIN_TURNS = 2
    const val MAX_TURNS = 3
}

/**
 * Raw audio to upload, deliberately free of any Retrofit/OkHttp type so the
 * recorder and domain layers don't depend on the network implementation.
 * Component D is trained on raw 16-bit PCM WAV — the recorder must NOT apply
 * noise suppression / AGC / echo cancellation, which reshape the prosody the
 * model reads.
 */
class AudioPayload(
    val bytes: ByteArray,
    val fileName: String = "clip.wav",
    val mimeType: String = "audio/wav",
)

data class BackendLayers(
    val quality: Boolean,
    val fusion: Boolean,
    val compare: Boolean,
    val crossmodal: Boolean,
    val anomaly: Boolean,
)

data class BackendHealth(val status: String, val layers: BackendLayers)

/** Layer-1 acoustic metrics, shared by ambient + infer quality. */
data class AudioMetrics(
    val durationSec: Double,
    val rms: Double,
    val clipRatio: Double,
    val speechSeconds: Double,
    val speechFraction: Double,
    val speechSegments: Int,
    val noiseFloorRms: Double? = null,   // Layer-1 steady floor (ambient only), for the adaptive VAD threshold
)

/** One Layer-1 acoustic check the app can render as a row (WP1). severity
 *  "fail" gates the room; "warn" is advisory. */
data class AmbientCheck(
    val id: String,
    val label: String,
    val value: Double,
    val unit: String,
    val pass: Boolean,
    val severity: String,   // "fail" | "warn"
    val message: String,
)

/** Layer 1 — room quality gate before a real recording. [score]/[noiseType]/
 *  [checks] are additive (older servers omit them; defaults keep the app working). */
data class AmbientResult(
    val ok: Boolean,
    val reasons: List<String>,
    val metrics: AudioMetrics?,
    val score: Int = 0,
    val noiseType: String = "quiet",     // quiet | hum | broadband | hiss | intermittent | voices
    val checks: List<AmbientCheck> = emptyList(),
)

/** Component B's ordinal reading echoed by /infer when poll_b=true (else null). */
data class BodyReading(
    val level: String,          // "no" | "mild" | "moderate" | "high"
    val confidence: Double,
    val source: String,
)

/** Layer 2 — one voice-stress reading for a phase. */
data class VoiceAnalysis(
    val sessionId: String,
    val stressScore: Double,        // 0–10 continuous
    val stressLevel: String,        // no | mild | moderate | high
    val stressType: String?,        // activated | shutdown | null
    val confidence: Double,
    val valence: Double,
    val arousal: Double,
    val gateMean: Double,
    val quality: AudioMetrics?,
    val body: BodyReading?,         // only when poll_b=true and B responded
    val inputLevel: String?,        // "faint" when the clip was quiet
    val warnings: List<String>,
)

data class CompanionReply(val reply: String)

/**
 * One turn of `/companion/voice-turn`: the STT transcript of what the person
 * said, the companion's spoken reply, whether Layer 1 accepted the clip, and —
 * only on the final (is_final=true) call — the scored [analysis]. [crisis] is
 * true when the transcript tripped the server's crisis net (reply == CRISIS_REPLY).
 */
data class VoiceTurnResult(
    val transcript: String,
    val reply: String,
    val crisis: Boolean,
    val accepted: Boolean,
    val reasons: List<String>,
    val analysis: VoiceAnalysis?,
    val sessionId: String,
)

/** Layer 3 — within-speaker pre→post change (the primary, reliable signal). */
data class StressComparison(
    val preStress: Double,
    val postStress: Double,
    val delta: Double,
    val direction: String,          // improved | worsened | no_reliable_change
    val improved: Boolean,
    val magnitude: String,
    val reliable: Boolean,
    val meanConfidence: Double,
)

data class ModalTrend(
    val pre: Double,
    val post: Double,
    val trend: Double,
    val confidencePre: Double,
    val confidencePost: Double,
)

/** Layer 4 — voice × HRV cross-check. Null when Component B data was absent. */
data class CrossModalResult(
    val validated: Boolean,
    val agreement: Double,
    val mismatchType: String?,
    val voice: ModalTrend,
    val body: ModalTrend,
    val lowConfidence: Boolean,
    val deferredTo: String?,
    val unresolvedMismatch: String?,
    val note: String?,
)

/** Layer 5 — session anomaly. Null when the anomaly model wasn't loaded. */
data class AnomalyResult(
    val anomaly: Boolean,
    val anomalyDirection: String?,   // unusual_improvement | unusual_worsening
    val severity: String,            // none | mild | moderate | severe
    val reasons: List<String>,
    val error: Double,
    val threshold: Double,
    val personalised: Boolean,
)

data class PersonalBaseline(
    val personalised: Boolean,
    val baseline: Double?,
    val deviation: Double?,
    val z: Double?,
    val relativeBand: String?,       // e.g. "typical for you"
    val note: String?,
)

/**
 * Speaker-relative verdict. The primary signal is the pre→post CHANGE, not the
 * absolute level — the app must present it that way (never as a diagnosis).
 */
data class SessionVerdict(
    val primarySignal: String,       // "change"
    val sessionHelped: Boolean,
    val direction: String,
    val reliable: Boolean,
    val note: String,
)

/** Layers 3+4+5 combined — the one report the app shows after the post recording. */
data class SessionReport(
    val stressLevel: Double,
    val confidence: Double,
    val verdict: SessionVerdict,
    val comparison: StressComparison,
    val crossmodal: CrossModalResult?,
    val anomaly: AnomalyResult?,
    val personalBaseline: PersonalBaseline,
)
