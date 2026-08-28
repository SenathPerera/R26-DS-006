package com.mindsyncvr.core.voice

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Handler
import android.os.Looper
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.ByteArrayOutputStream
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
class VoiceRecorder(private val sampleRate: Int = 16_000) {

    private var record: AudioRecord? = null
    private var worker: Thread? = null
    @Volatile private var active = false
    private val pcm = ByteArrayOutputStream()
    private val main = Handler(Looper.getMainLooper())

    private val _progress = MutableStateFlow(CaptureProgress())
    val progress: StateFlow<CaptureProgress> = _progress.asStateFlow()

    val isActive: Boolean get() = active

    @SuppressLint("MissingPermission")
    fun start(minSpeechSec: Int, maxSec: Int, silenceTailSec: Double, onResult: (AudioPayload?, Int) -> Unit) {
        if (active) return
        pcm.reset()
        val minBuf = AudioRecord.getMinBufferSize(
            sampleRate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT,
        ).coerceAtLeast(2048)

        val rec = openRecord(MediaRecorder.AudioSource.UNPROCESSED, minBuf)
            ?: openRecord(MediaRecorder.AudioSource.VOICE_RECOGNITION, minBuf)
            ?: run { main.post { onResult(null, 0) }; return }
        record = rec
        active = true
        rec.startRecording()

        worker = Thread {
            val frame = ShortArray(minBuf)
            var totalSamples = 0L
            var speechSamples = 0L
            var silenceSamples = 0L
            while (active) {
                val n = rec.read(frame, 0, frame.size)
                if (n <= 0) continue
                appendLittleEndian(frame, n)
                val rms = rawRms(frame, n)
                val speaking = rms > SPEECH_RMS
                totalSamples += n
                if (speaking) { speechSamples += n; silenceSamples = 0 } else silenceSamples += n

                val elapsedSec = (totalSamples / sampleRate).toInt()
                val speechSec = (speechSamples / sampleRate).toInt()
                _progress.value = CaptureProgress(
                    active = true, speaking = speaking, elapsedSec = elapsedSec,
                    speechSec = speechSec, amplitude = min(1.0, rms * 5).toFloat(),
                )
                val trailingSilence = silenceSamples.toDouble() / sampleRate
                val ended = speechSec >= minSpeechSec && trailingSilence >= silenceTailSec
                if (ended || elapsedSec >= maxSec) break
            }
            deliver(onResult, (speechSamples / sampleRate).toInt())
        }.also { it.start() }
    }

    /** Stop early (e.g. leaving the screen) without delivering a result. */
    fun cancel() {
        active = false
        try { worker?.join(300) } catch (_: InterruptedException) { /* ignore */ }
        worker = null
        teardown()
        pcm.reset()
        _progress.value = CaptureProgress()
    }

    private fun deliver(onResult: (AudioPayload?, Int) -> Unit, speechSec: Int) {
        active = false
        teardown()
        val data = pcm.toByteArray()
        val payload = if (data.size < 3200) null    // < ~0.1s captured
        else AudioPayload(bytes = wrapWav(data), fileName = "checkin.wav", mimeType = "audio/wav")
        _progress.value = CaptureProgress()
        main.post { onResult(payload, speechSec) }
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

    private fun wrapWav(pcmData: ByteArray): ByteArray {
        val byteRate = sampleRate * 2
        val header = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN)
        header.put("RIFF".toByteArray(Charsets.US_ASCII))
        header.putInt(36 + pcmData.size)
        header.put("WAVE".toByteArray(Charsets.US_ASCII))
        header.put("fmt ".toByteArray(Charsets.US_ASCII))
        header.putInt(16); header.putShort(1); header.putShort(1)
        header.putInt(sampleRate); header.putInt(byteRate); header.putShort(2); header.putShort(16)
        header.put("data".toByteArray(Charsets.US_ASCII))
        header.putInt(pcmData.size)
        return header.array() + pcmData
    }

    @SuppressLint("MissingPermission")
    private fun openRecord(source: Int, minBuf: Int): AudioRecord? {
        val rec = runCatching {
            AudioRecord(source, sampleRate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, minBuf * 2)
        }.getOrNull() ?: return null
        if (rec.state != AudioRecord.STATE_INITIALIZED) { rec.release(); return null }
        return rec
    }

    private companion object {
        // Energy VAD threshold (normalised RMS). Speech typically sits well above
        // this; the backend's Silero VAD is the real gate — this only decides when
        // to stop listening. Tuned conservatively so quiet rooms don't false-trigger.
        const val SPEECH_RMS = 0.018
    }
}
