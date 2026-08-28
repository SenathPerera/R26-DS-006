package com.mindsyncvr.core.voice

import kotlinx.coroutines.delay

/**
 * The app's gateway to Component D. UI/ViewModel talk to this in domain terms
 * and never see Retrofit. The same [sessionId] threads every call for one Cognify
 * session (pre → VR → post → report) — it is the correlation key Component D uses.
 *
 * All calls return [Result]; failures carry a typed [VoiceError] (see
 * [Throwable.voiceError]).
 */
interface VoiceStressRepository {

    suspend fun health(): Result<BackendHealth>

    /** Layer 1 — is the room quiet enough to record? */
    suspend fun ambientCheck(audio: AudioPayload): Result<AmbientResult>

    /**
     * Layer 2 — score one voice clip for a phase.
     * @param pollB pull Component B's live HRV reading at this moment (default off;
     *   D falls back to its simulated provider so Layer 4 still cross-checks).
     */
    suspend fun analyzeVoice(
        sessionId: String,
        phase: SessionPhase,
        audio: AudioPayload,
        userId: String? = null,
        language: String? = null,
        pollB: Boolean = false,
        log: Boolean = false,
    ): Result<VoiceAnalysis>

    /** One conversational turn with the health companion (text in, reply out). */
    suspend fun companionMessage(
        sessionId: String,
        text: String,
        phase: SessionPhase,
    ): Result<CompanionReply>

    /** Layers 3+4+5 — the final report, after both pre and post analysis exist. */
    suspend fun completeSession(
        sessionId: String,
        userId: String? = null,
        useMockHrv: Boolean = true,
        language: String? = null,
        selfReportPre: Double? = null,
        selfReportPost: Double? = null,
        log: Boolean = false,
    ): Result<SessionReport>
}

/**
 * Offline stand-in so the app builds and demos with no Component D running —
 * matching how the rest of the app defaults to mocks. Fakes a plausible calming
 * curve (post stress lower than pre), like [com.mindsyncvr.core.realtime.MockRealtimeSessionClient].
 */
class MockVoiceStressRepository : VoiceStressRepository {

    override suspend fun health(): Result<BackendHealth> {
        delay(120)
        return Result.success(
            BackendHealth("ok", BackendLayers(quality = true, fusion = true, compare = true, crossmodal = true, anomaly = true)),
        )
    }

    override suspend fun ambientCheck(audio: AudioPayload): Result<AmbientResult> {
        delay(200)
        return Result.success(AmbientResult(ok = true, reasons = emptyList(), metrics = SAMPLE_METRICS))
    }

    override suspend fun analyzeVoice(
        sessionId: String,
        phase: SessionPhase,
        audio: AudioPayload,
        userId: String?,
        language: String?,
        pollB: Boolean,
        log: Boolean,
    ): Result<VoiceAnalysis> {
        delay(400)
        val score = if (phase == SessionPhase.Pre) 6.4 else 3.1
        return Result.success(
            VoiceAnalysis(
                sessionId = sessionId,
                stressScore = score,
                stressLevel = if (score >= 5) "moderate" else "mild",
                stressType = if (phase == SessionPhase.Pre) "activated" else null,
                confidence = 0.71,
                valence = if (phase == SessionPhase.Pre) -0.34 else 0.28,
                arousal = 0.12,
                gateMean = 0.5,
                quality = SAMPLE_METRICS,
                body = null,
                inputLevel = null,
                warnings = emptyList(),
            ),
        )
    }

    override suspend fun companionMessage(sessionId: String, text: String, phase: SessionPhase): Result<CompanionReply> {
        delay(300)
        val reply = if (phase == SessionPhase.Pre) {
            "It sounds like today has felt heavy on you. What's been the biggest weight?"
        } else {
            "It sounds a little lighter now. What shifted for you in there?"
        }
        return Result.success(CompanionReply(reply))
    }

    override suspend fun completeSession(
        sessionId: String,
        userId: String?,
        useMockHrv: Boolean,
        language: String?,
        selfReportPre: Double?,
        selfReportPost: Double?,
        log: Boolean,
    ): Result<SessionReport> {
        delay(500)
        return Result.success(
            SessionReport(
                stressLevel = 3.1,
                confidence = 0.71,
                verdict = SessionVerdict("change", sessionHelped = true, direction = "improved", reliable = true, note = "Primary signal is the within-speaker pre→post change."),
                comparison = StressComparison(6.4, 3.1, -3.3, "improved", improved = true, magnitude = "large", reliable = true, meanConfidence = 0.71),
                crossmodal = null,
                anomaly = null,
                personalBaseline = PersonalBaseline(personalised = false, baseline = null, deviation = null, z = null, relativeBand = null, note = "learning your baseline (0/3 sessions)"),
            ),
        )
    }

    private companion object {
        val SAMPLE_METRICS = AudioMetrics(durationSec = 12.0, rms = 0.031, clipRatio = 0.0, speechSeconds = 9.4, speechFraction = 0.78, speechSegments = 4)
    }
}
