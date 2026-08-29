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

    /**
     * One spoken conversational turn: uploads the clip, which the server
     * transcribes (STT) and replies to; on [isFinal] it also scores the clip and
     * stores it so [completeSession] works. The companion reflects on what the
     * person actually said — no hardcoded seed text (BUG-2).
     */
    suspend fun voiceTurn(
        sessionId: String,
        phase: SessionPhase,
        audio: AudioPayload,
        isFinal: Boolean,
        userId: String? = null,
        language: String? = null,
        pollB: Boolean = false,
        log: Boolean = false,
    ): Result<VoiceTurnResult>

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
        return Result.success(
            AmbientResult(
                ok = true, reasons = emptyList(), metrics = SAMPLE_METRICS,
                score = 88, noiseType = "quiet",
                checks = listOf(
                    AmbientCheck("noise_floor", "Background noise", -54.0, "dBFS", true, "fail", "The room is quiet enough for a clean recording."),
                    AmbientCheck("peaks", "Sudden sounds", -46.0, "dBFS", true, "fail", "No sudden sounds — good."),
                    AmbientCheck("voices", "Nearby speech", 0.0, "s", true, "fail", "No nearby voices — good."),
                    AmbientCheck("tonal_noise", "Hum", 0.2, "ratio", true, "warn", "No tonal hum — good."),
                    AmbientCheck("clipping", "Distortion", 0.0, "ratio", true, "fail", "No distortion — good."),
                    AmbientCheck("duration", "Sample length", 8.0, "s", true, "fail", "Enough audio to judge — good."),
                ),
            ),
        )
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

    override suspend fun voiceTurn(
        sessionId: String,
        phase: SessionPhase,
        audio: AudioPayload,
        isFinal: Boolean,
        userId: String?,
        language: String?,
        pollB: Boolean,
        log: Boolean,
    ): Result<VoiceTurnResult> {
        delay(300)
        val transcript = if (phase == SessionPhase.Pre) {
            "Honestly today was rough, I barely slept and I've got a deadline hanging over me."
        } else {
            "I feel a bit lighter now, my shoulders aren't as tight as before."
        }
        val analysis = if (isFinal) analyzeVoice(sessionId, phase, audio, userId, language, pollB, log).getOrNull() else null
        val reply = if (phase == SessionPhase.Pre) {
            "It sounds like the deadline and the lost sleep are piling up. What part of it weighs the most?"
        } else {
            "That easing in your shoulders is worth noticing. What feels different now?"
        }
        return Result.success(
            VoiceTurnResult(
                transcript = transcript, reply = reply, crisis = false,
                accepted = true, reasons = emptyList(), analysis = analysis, sessionId = sessionId,
            ),
        )
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
