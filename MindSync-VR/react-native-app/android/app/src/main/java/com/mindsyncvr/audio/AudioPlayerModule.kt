package com.mindsyncvr.audio

import android.media.MediaPlayer
import android.speech.tts.TextToSpeech
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import com.facebook.react.bridge.WritableMap
import com.facebook.react.modules.core.DeviceEventManagerModule
import java.util.Locale

/**
 * Gives the companion (Sarah) a voice.
 *
 * `speak` streams the server's ElevenLabs proxy (a realistic voice) via
 * MediaPlayer; if that fails - no API key, no network - it falls back to the
 * phone's built-in Text-To-Speech so the companion ALWAYS speaks. It resolves
 * with the clip duration so the JS side can reveal her words in time with the
 * audio, and emits a start event so the avatar can switch to its speaking state.
 */
class AudioPlayerModule(reactContext: ReactApplicationContext) :
  ReactContextBaseJavaModule(reactContext) {

  override fun getName() = "AudioPlayer"

  private var player: MediaPlayer? = null
  private var tts: TextToSpeech? = null
  private var ttsReady = false

  @ReactMethod fun addListener(eventName: String) {}
  @ReactMethod fun removeListeners(count: Int) {}

  private fun emit(event: String, params: WritableMap) {
    reactApplicationContext
      .getJSModule(DeviceEventManagerModule.RCTDeviceEventEmitter::class.java)
      .emit(event, params)
  }

  /**
   * Speak `text`. Prefer the realistic voice at `url` (the /companion/tts
   * proxy); on any failure, speak `text` with on-device TTS. Resolves once
   * playback finishes: {source, durationMs}.
   */
  @ReactMethod
  fun speak(url: String?, text: String, language: String?, promise: Promise) {
    stopInternal()
    if (url.isNullOrBlank()) {
      speakDevice(text, language, promise)
      return
    }
    try {
      val mp = MediaPlayer()
      player = mp
      mp.setDataSource(url)
      mp.setOnPreparedListener {
        emit("AudioPlayer.start", Arguments.createMap().apply {
          putString("source", "eleven"); putInt("durationMs", it.duration)
        })
        it.start()
      }
      mp.setOnCompletionListener {
        val dur = it.duration
        releasePlayer()
        promise.resolve(Arguments.createMap().apply { putString("source", "eleven"); putInt("durationMs", dur) })
      }
      mp.setOnErrorListener { _, _, _ ->
        releasePlayer()
        speakDevice(text, language, promise)   // graceful fallback
        true
      }
      mp.prepareAsync()
    } catch (e: Exception) {
      releasePlayer()
      speakDevice(text, language, promise)
    }
  }

  private fun speakDevice(text: String, language: String?, promise: Promise) {
    val locale = if ((language ?: "").lowercase().startsWith("si")) Locale("si", "LK") else Locale.US
    // Rough duration estimate for the word-reveal timer (~370ms/word).
    val estMs = (text.split(" ").size * 370).coerceAtLeast(1200)
    emit("AudioPlayer.start", Arguments.createMap().apply { putString("source", "device"); putInt("durationMs", estMs) })

    fun run() {
      val t = tts ?: return promise.resolve(result("device", estMs))
      try { t.language = locale } catch (_: Exception) {}
      t.setOnUtteranceProgressListener(object : android.speech.tts.UtteranceProgressListener() {
        override fun onStart(id: String?) {}
        override fun onDone(id: String?) { promise.resolve(result("device", estMs)) }
        @Deprecated("deprecated") override fun onError(id: String?) { promise.resolve(result("device", estMs)) }
      })
      t.speak(text, TextToSpeech.QUEUE_FLUSH, null, "sarah")
    }

    if (ttsReady && tts != null) { run(); return }
    tts = TextToSpeech(reactApplicationContext) { status ->
      ttsReady = status == TextToSpeech.SUCCESS
      if (ttsReady) run() else promise.resolve(result("none", 0))
    }
  }

  private fun result(source: String, dur: Int): WritableMap =
    Arguments.createMap().apply { putString("source", source); putInt("durationMs", dur) }

  @ReactMethod
  fun stop(promise: Promise) {
    stopInternal()
    promise.resolve(true)
  }

  private fun stopInternal() {
    releasePlayer()
    try { tts?.stop() } catch (_: Exception) {}
  }

  private fun releasePlayer() {
    try { player?.reset(); player?.release() } catch (_: Exception) {}
    player = null
  }

  override fun onCatalystInstanceDestroy() {
    stopInternal()
    try { tts?.shutdown() } catch (_: Exception) {}
    tts = null
  }
}
