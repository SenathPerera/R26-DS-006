package com.mindsyncvr.core.voice

import android.content.Context
import android.speech.tts.TextToSpeech
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import java.util.Locale

/**
 * Free, on-device text-to-speech for the companion's voice (Android TextToSpeech).
 * The companion's reply text comes from Component D; this only speaks it aloud, so
 * no sensitive numbers or private notes are ever voiced (the server already scrubs
 * those before the reply leaves it).
 */
class TtsSpeaker(context: Context) {
    private var ready = false
    private val tts = TextToSpeech(context.applicationContext) { status ->
        if (status == TextToSpeech.SUCCESS) { ready = true }
    }.also { it.language = Locale.US }

    fun speak(text: String) {
        if (ready && text.isNotBlank()) {
            tts.speak(text, TextToSpeech.QUEUE_FLUSH, null, "companion")
        }
    }

    fun stop() = tts.stop()
    fun shutdown() { tts.stop(); tts.shutdown() }
}

/** Remembers a [TtsSpeaker] and shuts it down when the composable leaves. */
@Composable
fun rememberTtsSpeaker(): TtsSpeaker {
    val context = LocalContext.current
    val speaker = remember { TtsSpeaker(context) }
    DisposableEffect(Unit) { onDispose { speaker.shutdown() } }
    return speaker
}
