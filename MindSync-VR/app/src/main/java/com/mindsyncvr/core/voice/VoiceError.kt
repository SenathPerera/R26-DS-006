package com.mindsyncvr.core.voice

/**
 * Domain errors for the voice pipeline. Technical failures (HTTP codes, socket
 * exceptions, malformed JSON) are mapped to these so the UI can speak human, and
 * so Component D's meaningful codes aren't flattened into "server error":
 *  - 422 -> [AudioRejected]  (Layer-1 quality gate: retry the recording)
 *  - 404 -> [SessionIncomplete] (asked for a report before both pre+post exist)
 *  - 503 -> [BackendUnavailable] (a model/companion isn't loaded)
 */
sealed class VoiceError {
    /** No connectivity / host unreachable / DNS / TLS handshake failure. */
    object NetworkUnavailable : VoiceError()
    object Timeout : VoiceError()

    /** Layer 1 rejected the clip. `reasons` are diagnostic — the UI shows a
     *  gentle "let's try that recording again", not these raw strings. */
    data class AudioRejected(val reasons: List<String>) : VoiceError()

    /** 400 — the clip was empty / no audio was captured (distinct from a clip
     *  that recorded fine but Layer 1 rejected). `reasons` come from the server. */
    data class NoAudioCaptured(val reasons: List<String>) : VoiceError()

    /** /full-session before both pre and post /infer results exist. */
    object SessionIncomplete : VoiceError()

    /** A Component D model or the companion isn't available (fusion/anomaly/LLM). */
    data class BackendUnavailable(val detail: String?) : VoiceError()

    object Unauthorized : VoiceError()
    data class ServerError(val code: Int) : VoiceError()
    object Malformed : VoiceError()
    data class Unknown(val message: String?) : VoiceError()
}

/** Carrier so repositories can return `Result<T>` yet keep the typed [error]. */
class VoiceStressException(val error: VoiceError) : Exception(error.toString())

/** Convenience: the typed error behind a failed [Result], if any. */
val Throwable.voiceError: VoiceError?
    get() = (this as? VoiceStressException)?.error

/** A calm, non-technical line for the UI — never a stack trace or HTTP code. */
fun VoiceError.message(): String = when (this) {
    VoiceError.NetworkUnavailable -> "Can't reach the check-in service. Check your connection and try again."
    VoiceError.Timeout -> "That took too long — the service may still be warming up. Please try again."
    is VoiceError.AudioRejected -> "We couldn't get a clear enough recording. Let's try that again — somewhere quiet, speaking naturally."
    is VoiceError.NoAudioCaptured -> "I didn't catch any sound that time. Let's try again — a little closer to the microphone."
    VoiceError.SessionIncomplete -> "We need both the before and after recordings before the report."
    is VoiceError.BackendUnavailable -> "The check-in service isn't fully ready yet. Please try again in a moment."
    VoiceError.Unauthorized -> "This check-in isn't authorised."
    is VoiceError.ServerError -> "Something went wrong on the service. Please try again."
    VoiceError.Malformed -> "We got an unexpected response. Please try again."
    is VoiceError.Unknown -> "Something went wrong. Please try again."
}
