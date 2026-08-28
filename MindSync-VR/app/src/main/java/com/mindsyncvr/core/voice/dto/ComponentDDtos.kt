package com.mindsyncvr.core.voice.dto

import com.mindsyncvr.core.voice.AmbientResult
import com.mindsyncvr.core.voice.AnomalyResult
import com.mindsyncvr.core.voice.AudioMetrics
import com.mindsyncvr.core.voice.BackendHealth
import com.mindsyncvr.core.voice.BackendLayers
import com.mindsyncvr.core.voice.BodyReading
import com.mindsyncvr.core.voice.CompanionReply
import com.mindsyncvr.core.voice.CrossModalResult
import com.mindsyncvr.core.voice.ModalTrend
import com.mindsyncvr.core.voice.PersonalBaseline
import com.mindsyncvr.core.voice.SessionReport
import com.mindsyncvr.core.voice.SessionVerdict
import com.mindsyncvr.core.voice.StressComparison
import com.mindsyncvr.core.voice.VoiceAnalysis
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

// Wire DTOs — field names/shapes verified against component-d/server/main.py and
// the layer modules. Parsed with ignoreUnknownKeys, so additive backend changes
// don't break the client; each maps to a domain model via toDomain().

// ---- requests -------------------------------------------------------------

@Serializable
data class CompanionRequestDto(
    @SerialName("session_id") val sessionId: String,
    val text: String,
)

@Serializable
data class FullSessionRequestDto(
    @SerialName("session_id") val sessionId: String,
    @SerialName("user_id") val userId: String = "default",
    @SerialName("use_mock_hrv") val useMockHrv: Boolean = false,
    val language: String? = null,
    @SerialName("self_report_pre") val selfReportPre: Double? = null,
    @SerialName("self_report_post") val selfReportPost: Double? = null,
    val notes: String? = null,
    val log: Boolean = false,
)

// ---- responses ------------------------------------------------------------

@Serializable
data class HealthDto(val status: String, val layers: LayersDto) {
    fun toDomain() = BackendHealth(
        status = status,
        layers = BackendLayers(
            quality = layers.quality,
            fusion = layers.fusion,
            compare = layers.compare,
            crossmodal = layers.crossmodal,
            anomaly = layers.anomaly,
        ),
    )
}

@Serializable
data class LayersDto(
    @SerialName("layer1_quality") val quality: Boolean = false,
    @SerialName("layer2_fusion") val fusion: Boolean = false,
    @SerialName("layer3_compare") val compare: Boolean = false,
    @SerialName("layer4_crossmodal") val crossmodal: Boolean = false,
    @SerialName("layer5_anomaly") val anomaly: Boolean = false,
)

@Serializable
data class MetricsDto(
    @SerialName("duration_sec") val durationSec: Double = 0.0,
    val rms: Double = 0.0,
    @SerialName("clip_ratio") val clipRatio: Double = 0.0,
    @SerialName("speech_seconds") val speechSeconds: Double = 0.0,
    @SerialName("speech_fraction") val speechFraction: Double = 0.0,
    @SerialName("speech_segments") val speechSegments: Int = 0,
) {
    fun toDomain() = AudioMetrics(durationSec, rms, clipRatio, speechSeconds, speechFraction, speechSegments)
}

@Serializable
data class AmbientDto(
    val ok: Boolean = false,
    val reasons: List<String> = emptyList(),
    val metrics: MetricsDto? = null,
) {
    fun toDomain() = AmbientResult(ok, reasons, metrics?.toDomain())
}

@Serializable
data class BodyDto(
    val level: String,
    val confidence: Double,
    val source: String,
) {
    fun toDomain() = BodyReading(level, confidence, source)
}

@Serializable
data class InferDto(
    @SerialName("session_id") val sessionId: String,
    @SerialName("stress_score") val stressScore: Double,
    @SerialName("stress_level") val stressLevel: String,
    @SerialName("stress_type") val stressType: String? = null,
    val confidence: Double,
    val valence: Double,
    val arousal: Double,
    @SerialName("gate_mean") val gateMean: Double = 0.0,
    val quality: MetricsDto? = null,
    val body: BodyDto? = null,
    @SerialName("input_level") val inputLevel: String? = null,
    val warnings: List<String> = emptyList(),
) {
    fun toDomain() = VoiceAnalysis(
        sessionId = sessionId,
        stressScore = stressScore,
        stressLevel = stressLevel,
        stressType = stressType,
        confidence = confidence,
        valence = valence,
        arousal = arousal,
        gateMean = gateMean,
        quality = quality?.toDomain(),
        body = body?.toDomain(),
        inputLevel = inputLevel,
        warnings = warnings,
    )
}

@Serializable
data class CompanionReplyDto(val reply: String) {
    fun toDomain() = CompanionReply(reply)
}

@Serializable
data class VerdictDto(
    @SerialName("primary_signal") val primarySignal: String = "change",
    @SerialName("session_helped") val sessionHelped: Boolean = false,
    val direction: String = "",
    val reliable: Boolean = false,
    val note: String = "",
) {
    fun toDomain() = SessionVerdict(primarySignal, sessionHelped, direction, reliable, note)
}

@Serializable
data class ComparisonDto(
    @SerialName("pre_stress") val preStress: Double = 0.0,
    @SerialName("post_stress") val postStress: Double = 0.0,
    val delta: Double = 0.0,
    val direction: String = "",
    val improved: Boolean = false,
    val magnitude: String = "none",
    val reliable: Boolean = false,
    @SerialName("mean_confidence") val meanConfidence: Double = 0.0,
) {
    fun toDomain() = StressComparison(
        preStress, postStress, delta, direction, improved, magnitude, reliable, meanConfidence,
    )
}

@Serializable
data class ConfPairDto(val pre: Double = 0.0, val post: Double = 0.0)

@Serializable
data class ModalTrendDto(
    val pre: Double = 0.0,
    val post: Double = 0.0,
    val trend: Double = 0.0,
    val confidence: ConfPairDto = ConfPairDto(),
) {
    fun toDomain() = ModalTrend(pre, post, trend, confidence.pre, confidence.post)
}

@Serializable
data class CrossModalDto(
    val validated: Boolean = false,
    val agreement: Double = 0.0,
    @SerialName("mismatch_type") val mismatchType: String? = null,
    val voice: ModalTrendDto = ModalTrendDto(),
    val body: ModalTrendDto = ModalTrendDto(),
    @SerialName("low_confidence") val lowConfidence: Boolean = false,
    @SerialName("deferred_to") val deferredTo: String? = null,
    @SerialName("unresolved_mismatch") val unresolvedMismatch: String? = null,
    val note: String? = null,
) {
    fun toDomain() = CrossModalResult(
        validated = validated,
        agreement = agreement,
        mismatchType = mismatchType,
        voice = voice.toDomain(),
        body = body.toDomain(),
        lowConfidence = lowConfidence,
        deferredTo = deferredTo,
        unresolvedMismatch = unresolvedMismatch,
        note = note,
    )
}

@Serializable
data class AnomalyDto(
    val anomaly: Boolean = false,
    @SerialName("anomaly_direction") val anomalyDirection: String? = null,
    val severity: String = "none",
    val reasons: List<String> = emptyList(),
    val error: Double = 0.0,
    val threshold: Double = 0.0,
    val personalised: Boolean = false,
) {
    fun toDomain() = AnomalyResult(anomaly, anomalyDirection, severity, reasons, error, threshold, personalised)
}

@Serializable
data class PersonalBaselineDto(
    val personalised: Boolean = false,
    val baseline: Double? = null,
    val deviation: Double? = null,
    val z: Double? = null,
    @SerialName("relative_band") val relativeBand: String? = null,
    val note: String? = null,
) {
    fun toDomain() = PersonalBaseline(personalised, baseline, deviation, z, relativeBand, note)
}

@Serializable
data class FullSessionDto(
    @SerialName("stress_level") val stressLevel: Double,
    val confidence: Double,
    val verdict: VerdictDto,
    val comparison: ComparisonDto,
    val crossmodal: CrossModalDto? = null,
    val anomaly: AnomalyDto? = null,
    @SerialName("personal_baseline") val personalBaseline: PersonalBaselineDto = PersonalBaselineDto(),
) {
    fun toDomain() = SessionReport(
        stressLevel = stressLevel,
        confidence = confidence,
        verdict = verdict.toDomain(),
        comparison = comparison.toDomain(),
        crossmodal = crossmodal?.toDomain(),
        anomaly = anomaly?.toDomain(),
        personalBaseline = personalBaseline.toDomain(),
    )
}

/** 422 sends detail as an object; every other error sends a plain string. */
@Serializable
data class ErrorDetailDto(val error: String? = null, val reasons: List<String> = emptyList())
