package com.mindsyncvr.core.voice

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.media.MediaPlayer
import android.os.Build
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import android.util.Log
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.net.URLEncoder
import java.util.Locale
import java.util.concurrent.ConcurrentHashMap
import kotlin.coroutines.resume

/**
 * On-device text-to-speech for the companion's voice (Android TextToSpeech).
 *
 * The companion's reply text comes from Component D; this only speaks it aloud,
 * so no sensitive numbers or private notes are ever voiced (the server already
 * scrubs those before the reply leaves it).
 *
 * CRITICAL for strict turn-taking (BUG-1/BUG-5): callers MUST be able to wait
 * until speech has actually finished before opening the microphone. So this
 * exposes [awaitReady] (engine initialised — no more silently dropped first
 * line) and [speakAndWait] (suspends until the utterance is fully spoken), and
 * holds transient audio focus while speaking. A hard timeout guarantees a broken
 * TTS engine can never deadlock the flow.
 */
class TtsSpeaker(context: Context, private val ttsBaseUrl: String? = null) {

    private val appContext = context.applicationContext
    private val audioManager = appContext.getSystemService(Context.AUDIO_SERVICE) as AudioManager
    private var focusRequest: AudioFocusRequest? = null

    // Resumed from the OnInitListener; null once resolved.
    @Volatile private var initCallback: ((Boolean) -> Unit)? = null
    @Volatile private var initState: Boolean? = null

    // utteranceId -> completion callback (done or error). Concurrent because the
    // progress listener fires on a TTS-internal thread.
    private val pending = ConcurrentHashMap<String, (Boolean) -> Unit>()

    private val tts = TextToSpeech(appContext) { status ->
        val ok = status == TextToSpeech.SUCCESS
        if (ok) configureVoice()
        initState = ok
        initCallback?.invoke(ok)
        initCallback = null
    }.also {
        it.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
            override fun onStart(utteranceId: String?) {}
            override fun onDone(utteranceId: String?) { complete(utteranceId, true) }
            @Deprecated("deprecated in API 21")
            override fun onError(utteranceId: String?) { complete(utteranceId, false) }
            override fun onError(utteranceId: String?, errorCode: Int) { complete(utteranceId, false) }
        })
    }

    private fun configureVoice() {
        // Natural-voice settings mirrored from the proven web client (media.js):
        // a slightly slower rate and gentle pitch read as warm rather than robotic.
        tts.setSpeechRate(0.92f)
        tts.setPitch(1.02f)
        val locale = Locale.US
        if (tts.isLanguageAvailable(locale) >= TextToSpeech.LANG_AVAILABLE) {
            tts.language = locale
        }
    }

    private fun complete(utteranceId: String?, ok: Boolean) {
        utteranceId ?: return
        pending.remove(utteranceId)?.invoke(ok)
    }

    /** Suspend until the TTS engine is initialised. Returns false on timeout or
     *  init failure — callers proceed anyway (never block the flow forever). */
    suspend fun awaitReady(timeoutMs: Long = 3_000): Boolean {
        initState?.let { return it }
        val ready = withTimeoutOrNull(timeoutMs) {
            suspendCancellableCoroutine<Boolean> { cont ->
                initState?.let { if (cont.isActive) cont.resume(it); return@suspendCancellableCoroutine }
                initCallback = { value -> if (cont.isActive) cont.resume(value) }
                cont.invokeOnCancellation { initCallback = null }
            }
        }
        return ready ?: false
    }

    /**
     * Speak [text] and suspend until it has fully finished (onDone/onError), so
     * the caller can safely open the microphone afterwards. Holds transient audio
     * focus for the duration. A hard timeout (~2s + 120ms/char, capped 30s) means
     * a stuck engine degrades to "continue" rather than a deadlock.
     */
    suspend fun speakAndWait(text: String, language: String = "english") {
        if (text.isBlank()) return
        // Prefer the realistic server voice (ElevenLabs proxy). If it isn't
        // configured or fails, fall back to on-device TTS so the companion always
        // speaks — and turn-taking still waits for playback to finish either way.
        if (playServerVoice(text, language)) return
        if (!awaitReady()) { Log.w(TAG, "TTS not ready; skipping utterance"); return }

        requestFocus()
        try {
            val id = "companion-${System.nanoTime()}"
            val timeoutMs = (2_000L + text.length * 120L).coerceAtMost(30_000L)
            val finished = withTimeoutOrNull(timeoutMs) {
                suspendCancellableCoroutine<Boolean> { cont ->
                    pending[id] = { ok -> if (cont.isActive) cont.resume(ok) }
                    cont.invokeOnCancellation { pending.remove(id) }
                    val res = tts.speak(text, TextToSpeech.QUEUE_FLUSH, null, id)
                    if (res != TextToSpeech.SUCCESS) { pending.remove(id); if (cont.isActive) cont.resume(false) }
                }
            }
            if (finished == null) Log.w(TAG, "TTS timed out after ${timeoutMs}ms; continuing")
        } finally {
            abandonFocus()
        }
    }

    /** Stream the realistic companion voice from the server's /companion/tts proxy
     *  and suspend until it finishes. Returns false (→ on-device fallback) if no
     *  base URL is set, the server has no key (503), or playback fails. */
    private suspend fun playServerVoice(text: String, language: String): Boolean {
        val base = ttsBaseUrl?.takeIf { it.isNotBlank() } ?: return false
        val url = base.trimEnd('/') +
            "/companion/tts?text=" + URLEncoder.encode(text, "UTF-8") +
            "&language=" + URLEncoder.encode(language, "UTF-8")
        return withContext(Dispatchers.IO) {
            val mp = MediaPlayer()
            try {
                mp.setAudioAttributes(
                    AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_ASSISTANT)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build(),
                )
                mp.setDataSource(url)
                mp.prepare()                         // throws on 503 / network error → fallback
                requestFocus()
                val done = CompletableDeferred<Boolean>()
                mp.setOnCompletionListener { done.complete(true) }
                mp.setOnErrorListener { _, _, _ -> done.complete(false); true }
                mp.start()
                val timeoutMs = (3_000L + text.length * 120L).coerceAtMost(30_000L)
                withTimeoutOrNull(timeoutMs) { done.await() } ?: false
            } catch (e: Exception) {
                Log.w(TAG, "server voice unavailable, using on-device TTS: ${e.message}")
                false
            } finally {
                runCatching { mp.release() }
                abandonFocus()
            }
        }
    }

    private fun requestFocus() {
        val attrs = AudioAttributes.Builder()
            .setUsage(AudioAttributes.USAGE_ASSISTANT)
            .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
            .build()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val req = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT)
                .setAudioAttributes(attrs).build()
            focusRequest = req
            audioManager.requestAudioFocus(req)
        } else {
            @Suppress("DEPRECATION")
            audioManager.requestAudioFocus(null, AudioManager.STREAM_MUSIC,
                AudioManager.AUDIOFOCUS_GAIN_TRANSIENT)
        }
    }

    private fun abandonFocus() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            focusRequest?.let { audioManager.abandonAudioFocusRequest(it) }
            focusRequest = null
        } else {
            @Suppress("DEPRECATION")
            audioManager.abandonAudioFocus(null)
        }
    }

    fun stop() {
        tts.stop()
        pending.keys.toList().forEach { complete(it, false) }
        abandonFocus()
    }

    fun shutdown() {
        stop()
        tts.shutdown()
    }

    private companion object { const val TAG = "MindSyncVoice" }
}

/** Remembers a [TtsSpeaker] and shuts it down when the composable leaves.
 *  [ttsBaseUrl] enables the realistic server voice; null keeps on-device TTS. */
@Composable
fun rememberTtsSpeaker(ttsBaseUrl: String? = null): TtsSpeaker {
    val context = LocalContext.current
    val speaker = remember(ttsBaseUrl) { TtsSpeaker(context, ttsBaseUrl) }
    DisposableEffect(Unit) { onDispose { speaker.shutdown() } }
    return speaker
}
