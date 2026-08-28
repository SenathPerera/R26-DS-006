package com.mindsyncvr.core.model

import com.mindsyncvr.core.voice.AmbientResult
import com.mindsyncvr.core.voice.SessionReport
import com.mindsyncvr.core.voice.VoiceAnalysis

/**
 * UI state for Component D's voice flow, driven entirely by the health companion:
 *
 *   Environment (Layer 1) -> PreConversation (Layer 2, auto-recorded) ->
 *   VrSession (teammate) -> PostConversation (Layer 2) -> Report (Layers 3+4+5)
 *
 * Recording is automatic: when [awaitingAmbient] or [awaitingCapture] is true the
 * screen starts listening; [captureToken] bumps to re-arm listening when the
 * person hasn't spoken enough yet. Everything lives in memory only.
 */

enum class VoiceStage { Intro, Environment, PreConversation, VrSession, PostConversation, Report }

/** One line of the companion conversation (the person's or the companion's). */
data class CompanionTurn(val fromUser: Boolean, val text: String)

data class VoiceCheckInState(
    val active: Boolean = false,
    val stage: VoiceStage = VoiceStage.Environment,
    val sessionId: String? = null,
    val personName: String = "there",
    val language: String = "english",   // "english" | "sinhala" — chosen in the Intro step

    // Layer 1 — surrounding environment
    val checkingAmbient: Boolean = false,
    val ambientOk: Boolean? = null,
    val awaitingAmbient: Boolean = false,
    val ambientAttempts: Int = 0,       // failed room checks (gate stays closed — no skip)
    val ambient: AmbientResult? = null, // last room reading — metrics + score shown in the UI
    val ambientBestScore: Int? = null,  // best room score across attempts (shown after 3 fails)
    val speechThresholdRms: Double = 0.008,  // adaptive VAD threshold, calibrated from the room floor (WP2)

    // Layer 2 — spoken conversation (pre / post)
    val conversationPre: List<CompanionTurn> = emptyList(),
    val conversationPost: List<CompanionTurn> = emptyList(),
    val awaitingCapture: Boolean = false,
    val captureToken: Int = 0,          // bump to (re)arm automatic listening
    val escalationIndex: Int = 0,       // which "draw them out" rung is next
    val turnCount: Int = 0,             // spoken turns this phase (5-turn escape)
    val capturedSpeechSec: Int = 0,     // CUMULATIVE voiced speech this phase
    val lowConfidenceCapture: Boolean = false,  // escape hatch fired (too little speech)

    val pre: VoiceAnalysis? = null,
    val post: VoiceAnalysis? = null,
    val report: SessionReport? = null,

    val companionThinking: Boolean = false,
    val analyzing: Boolean = false,
    val generatingReport: Boolean = false,

    // Crisis — a distinct, terminal UI state: the companion's calm reply is shown,
    // scoring stops, and support information is surfaced (never continues to /infer).
    val crisis: Boolean = false,
    val crisisReply: String? = null,

    val backendHealthy: Boolean? = null,
    val error: String? = null,

    // Debug-only: force simulated HRV so all five layers can be demoed when
    // Component B isn't connected. Off by default; the toggle is never shown in
    // release builds. In production Layer 4 uses B, or honestly reports "unavailable".
    val debugForceMockHrv: Boolean = false,
)
