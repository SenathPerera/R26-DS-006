package com.mindsyncvr.audio

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.media.audiofx.AutomaticGainControl
import android.media.audiofx.NoiseSuppressor
import android.net.Uri
import android.provider.OpenableColumns
import androidx.core.content.ContextCompat
import com.facebook.react.bridge.ActivityEventListener
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import com.facebook.react.bridge.ReadableMap
import com.facebook.react.bridge.WritableMap
import com.facebook.react.modules.core.DeviceEventManagerModule
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sqrt

/**
 * Raw-voice recorder for the Component D voice check-in.
 *
 * Captures 16-bit PCM mono via AudioRecord with the MIC source and every
 * on-device audio effect (AGC / noise-suppression) explicitly disabled — the
 * exact capture the emotion2vec pipeline was validated on. AGC and NS reshape
 * prosody (F0, jitter, shimmer) the model reads and were never applied to the
 * training data, so they must stay off. Output is a self-contained WAV file the
 * JS layer posts straight to the backend.
 *
 * Adaptive auto-stop mirrors the Kotlin reference: a minimum listen window so a
 * pause never ends the turn early, then a silence tail with hysteresis, keyed to
 * a speech threshold the caller derives from the Layer-1 ambient noise floor.
 */
class AudioRecorderModule(reactContext: ReactApplicationContext) :
  ReactContextBaseJavaModule(reactContext), ActivityEventListener {

  override fun getName() = "AudioRecorder"

  init { reactContext.addActivityEventListener(this) }

  private var thread: Thread? = null
  @Volatile private var recording = false
  @Volatile private var stopRequested = false
  private var stopPromise: Promise? = null
  private val lock = Any()

  private var pickPromise: Promise? = null

  companion object { private const val PICK_AUDIO = 0xA0D1 }

  /**
   * DEV-only demonstration path: let the presenter pick an existing audio file
   * (WAV/MP3/M4A/MP4/…) instead of recording live, and run it through the SAME
   * backend pipeline. The server decodes non-WAV formats via its ffmpeg fallback,
   * so we just copy the chosen file into the app cache and hand back a file URI.
   * Resolves {uri, name, durationMs, sampleRate}.
   */
  @ReactMethod
  fun pickAudioFile(promise: Promise) {
    val activity = reactApplicationContext.currentActivity
    if (activity == null) { promise.reject("no_activity", "No foreground activity to open the picker"); return }
    if (pickPromise != null) { promise.reject("busy", "A file pick is already in progress"); return }
    pickPromise = promise
    try {
      val intent = Intent(Intent.ACTION_GET_CONTENT).apply {
        type = "*/*"
        putExtra(Intent.EXTRA_MIME_TYPES, arrayOf("audio/*", "video/mp4", "application/octet-stream"))
        addCategory(Intent.CATEGORY_OPENABLE)
      }
      activity.startActivityForResult(Intent.createChooser(intent, "Choose an audio file"), PICK_AUDIO)
    } catch (e: Exception) {
      pickPromise = null
      promise.reject("pick_failed", e.message)
    }
  }

  override fun onActivityResult(activity: Activity, requestCode: Int, resultCode: Int, data: Intent?) {
    if (requestCode != PICK_AUDIO) return
    val promise = pickPromise ?: return
    pickPromise = null
    val uri = data?.data
    if (resultCode != Activity.RESULT_OK || uri == null) { promise.reject("cancelled", "No file chosen"); return }
    try {
      val name = queryDisplayName(uri) ?: "upload_${System.currentTimeMillis()}"
      val ext = name.substringAfterLast('.', "")
      val outName = "picked_${System.currentTimeMillis()}" + if (ext.isNotEmpty()) ".$ext" else ""
      val outFile = File(reactApplicationContext.cacheDir, outName)
      reactApplicationContext.contentResolver.openInputStream(uri).use { input ->
        FileOutputStream(outFile).use { output -> input?.copyTo(output) }
      }
      promise.resolve(Arguments.createMap().apply {
        putString("uri", "file://${outFile.absolutePath}")
        putString("name", name)
        putDouble("durationMs", 0.0)
        putInt("sampleRate", 16000)
      })
    } catch (e: Exception) {
      promise.reject("copy_failed", e.message)
    }
  }

  override fun onNewIntent(intent: Intent) {}

  private fun queryDisplayName(uri: Uri): String? = try {
    reactApplicationContext.contentResolver.query(uri, null, null, null, null)?.use { c ->
      val idx = c.getColumnIndex(OpenableColumns.DISPLAY_NAME)
      if (idx >= 0 && c.moveToFirst()) c.getString(idx) else null
    }
  } catch (_: Exception) { null }

  private fun emit(event: String, params: WritableMap) {
    reactApplicationContext
      .getJSModule(DeviceEventManagerModule.RCTDeviceEventEmitter::class.java)
      .emit(event, params)
  }

  // NativeEventEmitter on the JS side calls these; no-ops keep it from warning.
  @ReactMethod fun addListener(eventName: String) {}
  @ReactMethod fun removeListeners(count: Int) {}

  @ReactMethod
  fun isRecording(promise: Promise) = promise.resolve(recording)

  /** Copy text to the system clipboard — backs the Validate tab's "Copy raw
   *  data" (the research JSON goes here, never onto the screen). */
  @ReactMethod
  fun copyToClipboard(text: String) {
    try {
      val cm = reactApplicationContext.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
      cm.setPrimaryClip(android.content.ClipData.newPlainText("session data", text))
    } catch (_: Exception) { /* clipboard unavailable */ }
  }

  /**
   * Concatenate several recorded WAVs (all 16 kHz mono 16-bit) into ONE WAV, so
   * the §4 accumulation loop can capture speech across multiple turns and upload
   * a single clip. Reads each file's PCM (skipping its 44-byte header), joins
   * them, and writes one fresh WAV. Resolves {uri, durationMs, sampleRate}.
   */
  @ReactMethod
  fun concatWavs(paths: com.facebook.react.bridge.ReadableArray, promise: Promise) {
    try {
      val pcm = ByteArrayOutputStream()
      var sampleRate = 16000
      for (i in 0 until paths.size()) {
        val raw = paths.getString(i) ?: continue
        val path = raw.removePrefix("file://")
        val bytes = File(path).readBytes()
        if (bytes.size <= 44) continue
        // sample rate lives at byte offset 24 (little-endian) of the WAV header
        sampleRate = (bytes[24].toInt() and 0xff) or ((bytes[25].toInt() and 0xff) shl 8) or
          ((bytes[26].toInt() and 0xff) shl 16) or ((bytes[27].toInt() and 0xff) shl 24)
        pcm.write(bytes, 44, bytes.size - 44)
      }
      val joined = pcm.toByteArray()
      if (joined.isEmpty()) {
        promise.reject("empty", "No audio to concatenate")
        return
      }
      val file = writeWav(joined, sampleRate)
      promise.resolve(Arguments.createMap().apply {
        putString("uri", "file://${file.absolutePath}")
        putDouble("durationMs", joined.size / (sampleRate * 2.0) * 1000.0)
        putInt("sampleRate", sampleRate)
      })
    } catch (e: Exception) {
      promise.reject("concat_failed", e.message)
    }
  }

  /**
   * Begin capture. Resolves once recording has actually started.
   * options:
   *   sampleRate      Int   default 16000 (mono 16-bit)
   *   minDurationMs   Int   ignore silence before this (default 12000)
   *   silenceTailMs   Int   auto-stop after this much trailing silence (default 3000)
   *   maxDurationMs   Int   hard cap (default 60000)
   *   silenceThreshold Double  RMS 0..1 below which a frame counts as silence.
   *                             <= 0 disables auto-stop (fully manual stop()).
   */
  @ReactMethod
  fun start(options: ReadableMap, promise: Promise) {
    synchronized(lock) {
      if (recording) {
        promise.reject("already_recording", "A recording is already in progress")
        return
      }
      if (ContextCompat.checkSelfPermission(reactApplicationContext, Manifest.permission.RECORD_AUDIO)
        != PackageManager.PERMISSION_GRANTED) {
        promise.reject("permission_denied", "RECORD_AUDIO permission not granted")
        return
      }

      val sampleRate = if (options.hasKey("sampleRate")) options.getInt("sampleRate") else 16000
      val minDurationMs = if (options.hasKey("minDurationMs")) options.getInt("minDurationMs") else 12000
      val silenceTailMs = if (options.hasKey("silenceTailMs")) options.getInt("silenceTailMs") else 3000
      val maxDurationMs = if (options.hasKey("maxDurationMs")) options.getInt("maxDurationMs") else 60000
      val silenceThreshold = if (options.hasKey("silenceThreshold")) options.getDouble("silenceThreshold") else 0.0

      val minBuf = AudioRecord.getMinBufferSize(
        sampleRate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
      if (minBuf <= 0) {
        promise.reject("unsupported", "AudioRecord not available at ${sampleRate}Hz on this device")
        return
      }
      // ~200ms frames — small enough for a responsive waveform, big enough to
      // keep the read loop cheap.
      val frameSamples = max(sampleRate / 5, minBuf / 2)
      val bufferBytes = max(minBuf, frameSamples * 2)

      val recorder = try {
        AudioRecord(
          MediaRecorder.AudioSource.MIC, sampleRate,
          AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, bufferBytes)
      } catch (e: Exception) {
        promise.reject("init_failed", "Could not open the microphone: ${e.message}")
        return
      }
      if (recorder.state != AudioRecord.STATE_INITIALIZED) {
        recorder.release()
        promise.reject("init_failed", "Microphone failed to initialise")
        return
      }
      disableEffects(recorder.audioSessionId)

      recording = true
      stopRequested = false
      thread = Thread { captureLoop(recorder, sampleRate, frameSamples, minDurationMs, silenceTailMs, maxDurationMs, silenceThreshold) }
      thread!!.start()
      promise.resolve(true)
    }
  }

  /** Manual stop. Resolves with {uri, durationMs, sampleRate}. */
  @ReactMethod
  fun stop(promise: Promise) {
    synchronized(lock) {
      if (!recording) {
        promise.reject("not_recording", "No recording in progress")
        return
      }
      stopPromise = promise
      stopRequested = true
    }
  }

  /** Stop and discard — no file, no result. */
  @ReactMethod
  fun cancel(promise: Promise) {
    synchronized(lock) {
      if (!recording) {
        promise.resolve(false)
        return
      }
      stopPromise = null
      stopRequested = true
      promise.resolve(true)
    }
  }

  private fun disableEffects(sessionId: Int) {
    // Best-effort: turn OFF any AGC / noise-suppression effect bound to this
    // session so the captured prosody is the real voice.
    try {
      if (AutomaticGainControl.isAvailable()) AutomaticGainControl.create(sessionId)?.enabled = false
    } catch (_: Exception) {}
    try {
      if (NoiseSuppressor.isAvailable()) NoiseSuppressor.create(sessionId)?.enabled = false
    } catch (_: Exception) {}
  }

  private fun captureLoop(
    recorder: AudioRecord, sampleRate: Int, frameSamples: Int,
    minDurationMs: Int, silenceTailMs: Int, maxDurationMs: Int, silenceThreshold: Double,
  ) {
    val pcm = ByteArrayOutputStream()
    val buffer = ShortArray(frameSamples)
    val bytesPerMs = sampleRate * 2 / 1000.0
    var silentMs = 0.0
    val autoStop = silenceThreshold > 0.0
    var discard = false

    try {
      recorder.startRecording()
      while (recording && !stopRequested) {
        val read = recorder.read(buffer, 0, buffer.size)
        if (read <= 0) continue

        // Accumulate raw little-endian PCM16.
        var sumSq = 0.0
        for (i in 0 until read) {
          val s = buffer[i].toInt()
          pcm.write(s and 0xff)
          pcm.write((s shr 8) and 0xff)
          sumSq += (s.toDouble() * s.toDouble())
        }
        val rms = sqrt(sumSq / read) / 32768.0
        val elapsedMs = pcm.size() / bytesPerMs

        val level = WritableNativeLevel(min(1.0, rms * 4.0), elapsedMs)
        emit("AudioRecorder.level", level)

        if (autoStop && elapsedMs >= minDurationMs) {
          val frameMs = read / bytesPerMs
          if (rms < silenceThreshold) {
            silentMs += frameMs
            if (silentMs >= silenceTailMs) break
          } else {
            silentMs = 0.0 // hysteresis: any speech resets the tail
          }
        }
        if (elapsedMs >= maxDurationMs) break
      }
    } catch (e: Exception) {
      discard = true
      emit("AudioRecorder.error", Arguments.createMap().apply { putString("message", e.message ?: "capture failed") })
    } finally {
      try { recorder.stop() } catch (_: Exception) {}
      recorder.release()
    }

    synchronized(lock) {
      val cancelled = stopRequested && stopPromise == null
      recording = false
      val pending = stopPromise
      stopPromise = null
      stopRequested = false

      if (discard || cancelled) return

      val bytes = pcm.toByteArray()
      if (bytes.size < 1600) { // < ~50ms — essentially nothing captured
        val err = "No audio was captured — check the microphone permission and record again."
        pending?.reject("no_audio", err)
          ?: emit("AudioRecorder.error", Arguments.createMap().apply { putString("message", err) })
        return
      }

      val file = writeWav(bytes, sampleRate)
      val durationMs = (bytes.size / (sampleRate * 2.0) * 1000.0)
      val result = Arguments.createMap().apply {
        putString("uri", "file://${file.absolutePath}")
        putDouble("durationMs", durationMs)
        putInt("sampleRate", sampleRate)
      }
      if (pending != null) pending.resolve(result) else emit("AudioRecorder.finish", result)
    }
  }

  private fun WritableNativeLevel(level: Double, elapsedMs: Double): WritableMap =
    Arguments.createMap().apply {
      putDouble("level", level)
      putDouble("elapsedMs", elapsedMs)
    }

  private fun writeWav(pcm: ByteArray, sampleRate: Int): File {
    val file = File(reactApplicationContext.cacheDir, "checkin_${System.currentTimeMillis()}.wav")
    FileOutputStream(file).use { out ->
      val dataLen = pcm.size
      val byteRate = sampleRate * 2 // mono, 16-bit
      val header = ByteArray(44)
      fun putStr(o: Int, s: String) { for (i in s.indices) header[o + i] = s[i].code.toByte() }
      fun putInt(o: Int, v: Int) {
        header[o] = (v and 0xff).toByte(); header[o + 1] = ((v shr 8) and 0xff).toByte()
        header[o + 2] = ((v shr 16) and 0xff).toByte(); header[o + 3] = ((v shr 24) and 0xff).toByte()
      }
      fun putShort(o: Int, v: Int) { header[o] = (v and 0xff).toByte(); header[o + 1] = ((v shr 8) and 0xff).toByte() }
      putStr(0, "RIFF"); putInt(4, 36 + dataLen); putStr(8, "WAVE")
      putStr(12, "fmt "); putInt(16, 16); putShort(20, 1); putShort(22, 1)
      putInt(24, sampleRate); putInt(28, byteRate); putShort(32, 2); putShort(34, 16)
      putStr(36, "data"); putInt(40, dataLen)
      out.write(header)
      out.write(pcm)
    }
    return file
  }
}
