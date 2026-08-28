package com.mindsyncvr.core.voice

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Pure PCM/WAV helpers, shared by [VoiceRecorder] (per-turn capture) and
 * [com.mindsyncvr.core.data.MindSyncRepository] (concatenating several turns into
 * ONE clip for the final scoring call — BUG-3/WP4). Kept free of Android types so
 * it is unit-testable on the JVM.
 *
 * Format throughout: 16-bit PCM, mono, little-endian, [SAMPLE_RATE] Hz.
 */
object WavUtil {

    const val SAMPLE_RATE = 16_000

    /** Concatenate raw PCM chunks (in capture order) into one contiguous buffer.
     *  Splicing happens at the PCM level — never by stitching WAV files with their
     *  44-byte headers left in the middle. */
    fun concatPcm(chunks: List<ByteArray>): ByteArray {
        val out = ByteArrayOutputStream(chunks.sumOf { it.size })
        chunks.forEach { out.write(it) }
        return out.toByteArray()
    }

    /** Wrap raw 16-bit PCM in a single canonical 44-byte WAV header written AFTER
     *  the data length is known (so the header's sizes are always correct). */
    fun wrapWav(pcm: ByteArray, sampleRate: Int = SAMPLE_RATE): ByteArray {
        val byteRate = sampleRate * 2               // mono * 16-bit
        val header = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN)
        header.put("RIFF".toByteArray(Charsets.US_ASCII))
        header.putInt(36 + pcm.size)
        header.put("WAVE".toByteArray(Charsets.US_ASCII))
        header.put("fmt ".toByteArray(Charsets.US_ASCII))
        header.putInt(16); header.putShort(1); header.putShort(1)
        header.putInt(sampleRate); header.putInt(byteRate); header.putShort(2); header.putShort(16)
        header.put("data".toByteArray(Charsets.US_ASCII))
        header.putInt(pcm.size)
        return header.array() + pcm
    }
}

/**
 * When to stop drawing a person out and score what they've said. The budget is
 * CUMULATIVE across turns (BUG-3: nothing accumulated before, so a person who
 * spoke 4s + 5s + 4s never reached one 6s window and looped forever).
 */
object TurnPolicy {

    /** Score once we've asked at least MIN_TURNS questions AND have enough speech,
     *  or once we hit MAX_TURNS — so the companion asks 2–3 questions, never more. */
    fun shouldFinalize(cumulativeSpeechSec: Int, turnCount: Int): Boolean =
        turnCount >= CaptureParams.MAX_TURNS ||
            (turnCount >= CaptureParams.MIN_TURNS && cumulativeSpeechSec >= CaptureParams.TARGET_SPEECH_SEC)

    /** True when we finalize ONLY because we hit the ceiling, not because enough
     *  speech was captured — the report must then mark the reading low-confidence. */
    fun isEscapeHatch(cumulativeSpeechSec: Int, turnCount: Int): Boolean =
        cumulativeSpeechSec < CaptureParams.TARGET_SPEECH_SEC &&
            turnCount >= CaptureParams.MAX_TURNS
}
