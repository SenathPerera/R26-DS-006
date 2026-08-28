package com.mindsyncvr.core.voice

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Handler
import android.os.Looper
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.ByteArrayOutputStream
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.min
import kotlin.math.sqrt

/** Live progress of an automatic capture, for the on-screen "listening" UI. */
data class CaptureProgress(
    val active: Boolean = false,
    val speaking: Boolean = false,
    val elapsedSec: Int = 0,
    val speechSec: Int = 0,
    val amplitude: Float = 0f,   // 0..1 for a level meter
)

/**
 * Automatic, hands-free voice capture for Component D. There is NO record/stop
 * button: [start] begins listening immediately, keeps the raw audio, tracks how
 * much of it is actual speech (a simple energy VAD), and stops on its own when
 * either (a) there's been enough speech AND a trailing pause, or (b) the max
 * window elapses. The result is delivered once via `onResult(payload, speechSec)`.
 *
 * CRITICAL: capture stays raw — [MediaRecorder.AudioSource.UNPROCESSED] with no
 * noise-suppression/AGC/echo-cancellation, because those reshape the prosody the
 * stress model reads. WAV is 16-bit PCM mono. Caller must hold RECORD_AUDIO.
 */
class VoiceRecorder(
    private val sampleRate: Int = 16_000,
    // Debug-only: when set (debug builds pass context.filesDir/voice_debug), every
    // captured clip is written here with a timestamped name so a failing capture
    // can be pulled and re-scored via curl — settling "capture vs transport" (WP9).
    private val debugDir: File? = null,
) {

    private var record: AudioRecord? = null
    private var worker: Thread? = null
    @Volatile private var active = false
    // Set by cancel() so a worker that exits its loop does NOT deliver a result
    // for a capture the caller has abandoned (BUG-8: prevents an onResult race
    // against pcm.reset()).
    @Volatile private var cancelled = false
    private val pcm = ByteArrayOutputStream()
    private val main = Handler(Looper.getMainLooper())

    private val _progress = MutableStateFlow(CaptureProgress())
    val progress: StateFlow<CaptureProgress> = _progress.asStateFlow()

    val isActive: Boolean get() = active

    /**
     * @param onResult delivers (wav payload, raw PCM, speechSec). The raw PCM lets
     *   the flow concatenate several turns into ONE clip for final scoring (WP4).
     * @param noSpeechTimeoutSec end a turn with no speech at all after this long,
     *   so a silent person triggers a gentle follow-up instead of holding the mic
     *   open for the full [maxSec]. Defaulted off for the ambient (silence) check.
     */
    /**
     * @param minListenSec never end a turn (on the speech path) before this many
     *   seconds of wall clock, whatever the VAD thinks — the hard floor that kills
     *   the "cut off at 4 seconds" complaint (WP2).
     * @param speechThreshold normalised-RMS level to ENTER speech. The EXIT level
     *   is 60% of it (hysteresis), and speech is held for [SPEECH_HOLD_MS] after
     *   the level drops, so inter-word gaps and unvoiced consonants don't flip the
     *   state mid-sentence. Calibrated from the room's measured noise floor.
     */
    @SuppressLint("MissingPermission")
    fun start(minSpeechSec: Int, maxSec: Int, silenceTailSec: Double,
              noSpeechTimeoutSec: Int = Int.MAX_VALUE,
              minListenSec: Int = 0,
              speechThreshold: Double = SPEECH_RMS,
              onResult: (AudioPayload?, ByteArray?, Int) -> Unit) {
        if (active) return
        Log.i(TAG, "capture start: minListen=${minListenSec}s minSpeech=${minSpeechSec}s tail=${silenceTailSec}s threshold=${"%.4f".format(speechThreshold)}")
        pcm.reset()
        cancelled = false
        // getMinBufferSize returns a size in BYTES; the ShortArray frame is sized
        // in SHORTS, so halve it (BUG-8: previously the frame was 2x too large).
        val minBuf = AudioRecord.getMinBufferSize(
            sampleRate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT,
        ).coerceAtLeast(2048)
        val frameShorts = (minBuf / 2).coerceAtLeast(1024)

        // Prefer UNPROCESSED (raw prosody the model was trained on); fall back to
        // VOICE_RECOGNITION, then plain MIC so a device supporting neither of the
        // first two still records instead of dying with a null payload (BUG-8).
        val rec = openRecord(MediaRecorder.AudioSource.UNPROCESSED, minBuf, "UNPROCESSED")
            ?: openRecord(MediaRecorder.AudioSource.VOICE_RECOGNITION, minBuf, "VOICE_RECOGNITION")
            ?: openRecord(MediaRecorder.AudioSource.MIC, minBuf, "MIC")
            ?: run { Log.w(TAG, "no usable audio source"); main.post { onResult(null, null, 0) }; return }
        record = rec
        active = true
        rec.startRecording()

        val enterTh = speechThreshold
        val exitTh = speechThreshold * 0.6
        val holdSamples = (SPEECH_HOLD_MS / 1000.0 * sampleRate).toLong()
        worker = Thread {
            val frame = ShortArray(frameShorts)
            var totalSamples = 0L
            var speechSamples = 0L
            var silenceSamples = 0L
            var speakingState = false      // hysteresis state
            var belowSamples = 0L          // samples spent below exit threshold while "speaking"
            while (active) {
                val n = rec.read(frame, 0, frame.size)
                if (n <= 0) continue
                appendLittleEndian(frame, n)
                val rms = rawRms(frame, n)
                // Hysteresis: enter speech above enterTh; once speaking, only drop
                // back to silence after SPEECH_HOLD_MS continuously below exitTh, so
                // a 400ms inter-word gap or a soft consonant doesn't end the turn.
                if (!speakingState) {
                    if (rms > enterTh) { speakingState = true; belowSamples = 0 }
                } else {
                    if (rms < exitTh) { belowSamples += n; if (belowSamples >= holdSamples) speakingState = false }
                    else belowSamples = 0
                }
                totalSamples += n
                if (speakingState) { speechSamples += n; silenceSamples = 0 } else silenceSamples += n

                val elapsedSec = (totalSamples / sampleRate).toInt()
                val speechSec = (speechSamples / sampleRate).toInt()
                _progress.value = CaptureProgress(
                    active = true, speaking = speakingState, elapsedSec = elapsedSec,
                    speechSec = speechSec, amplitude = min(1.0, rms * 5).toFloat(),
                )
                val trailingSilence = silenceSamples.toDouble() / sampleRate
                // A turn ends only after the minimum listen window AND enough speech
                // AND a full trailing pause — all three, so no early cutoff (WP2).
                val ended = elapsedSec >= minListenSec &&
                    speechSec >= minSpeechSec && trailingSilence >= silenceTailSec
                val silentGiveUp = speechSec == 0 && elapsedSec >= noSpeechTimeoutSec
                if (ended || elapsedSec >= maxSec || silentGiveUp) {
                    Log.i(TAG, "capture end: reason=${if (silentGiveUp) "no_speech" else if (elapsedSec >= maxSec) "max_cap" else "done"} elapsed=${elapsedSec}s speech=${speechSec}s tailSilence=${"%.1f".format(trailingSilence)}s")
                    break
                }
            }
            deliver(onResult, (speechSamples / sampleRate).toInt())
        }.also { it.start() }
    }

    /** Stop early (e.g. leaving the screen) without delivering a result. */
    fun cancel() {
        cancelled = true
        active = false
        try { worker?.join(300) } catch (_: InterruptedException) { /* ignore */ }
        worker = null
        teardown()
        pcm.reset()
        _progress.value = CaptureProgress()
    }

    private fun deliver(onResult: (AudioPayload?, ByteArray?, Int) -> Unit, speechSec: Int) {
        active = false
        // A cancelled capture must never post a result (BUG-8): the caller has
        // abandoned it and pcm has been / will be reset out from under us.
        if (cancelled) return
        teardown()
        val data = pcm.toByteArray()
        val enough = data.size >= 3200              // >= ~0.1s captured
        val wav = if (enough) WavUtil.wrapWav(data) else null
        val payload = wav?.let { AudioPayload(it, "checkin.wav", "audio/wav") }
        val raw = if (enough) data else null        // raw PCM for cross-turn concatenation
        val durationSec = data.size / 2.0 / sampleRate
        Log.i(TAG, "capture done: bytes=${data.size} duration=${"%.2f".format(durationSec)}s speechSec=$speechSec delivered=${payload != null}")
        if (wav != null) dumpDebug(wav)
        _progress.value = CaptureProgress()
        main.post { onResult(payload, raw, speechSec) }
    }

    private fun dumpDebug(wav: ByteArray) {
        val dir = debugDir ?: return
        runCatching {
            if (!dir.exists()) dir.mkdirs()
            val f = File(dir, "capture_${System.currentTimeMillis()}.wav")
            f.writeBytes(wav)
            Log.i(TAG, "debug wav written: ${f.absolutePath} (${wav.size} bytes)")
        }.onFailure { Log.w(TAG, "debug wav dump failed: ${it.message}") }
    }

    private fun teardown() {
        record?.let { r ->
            try { r.stop() } catch (_: IllegalStateException) { /* already stopped */ }
            r.release()
        }
        record = null
    }

    private fun appendLittleEndian(samples: ShortArray, count: Int) {
        val bytes = ByteBuffer.allocate(count * 2).order(ByteOrder.LITTLE_ENDIAN)
        for (i in 0 until count) bytes.putShort(samples[i])
        pcm.write(bytes.array(), 0, count * 2)
    }

    private fun rawRms(samples: ShortArray, count: Int): Double {
        var sum = 0.0
        val step = (count / 512).coerceAtLeast(1)
        var counted = 0
        var i = 0
        while (i < count) { val s = samples[i] / 32768.0; sum += s * s; counted++; i += step }
        return sqrt(sum / counted.coerceAtLeast(1))
    }

    @SuppressLint("MissingPermission")
    private fun openRecord(source: Int, minBuf: Int, name: String): AudioRecord? {
        val rec = runCatching {
            AudioRecord(source, sampleRate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, minBuf * 2)
        }.getOrNull() ?: return null
        if (rec.state != AudioRecord.STATE_INITIALIZED) { rec.release(); return null }
        Log.i(TAG, "opened audio source=$name sampleRate=$sampleRate")
        return rec
    }

    private companion object {
        const val TAG = "MindSyncVoice"

        // Energy VAD threshold (normalised RMS). This only decides when a turn ends;
        // the backend's Silero VAD is the real quality gate. Tuned DOWN from 0.018 to
        // 0.008 after on-device testing: the Galaxy A9 with AudioSource.UNPROCESSED
        // under-gains the mic (speech peaks ~0.05, RMS ~0.01), so 0.018 saw speech as
        // silence and the flow never advanced. 0.008 sits safely above this device's
        // ~0.006 room-noise floor. Re-tune per device if speechSec reads 0 while talking.
        // Now only a DEFAULT: the flow passes an adaptive threshold derived from the
        // measured room noise floor (WP2). Kept as the fallback when no floor is known.
        const val SPEECH_RMS = 0.008
        // Once speaking, hold that state this long after the level drops below the
        // exit threshold, so natural mid-sentence pauses don't end the turn (WP2).
        const val SPEECH_HOLD_MS = 600L
    }
}
