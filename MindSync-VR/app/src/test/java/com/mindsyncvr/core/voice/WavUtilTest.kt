package com.mindsyncvr.core.voice

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Pure-JVM tests for the PCM/WAV helpers and the cumulative-speech policy that
 * fix BUG-3 (speech discarded per turn). No Android types involved.
 */
class WavUtilTest {

    @Test
    fun `wrapWav writes a correct canonical 16k mono 16-bit header`() {
        val pcm = ByteArray(16_000 * 2) { (it % 251).toByte() }   // ~1s of PCM
        val wav = WavUtil.wrapWav(pcm)
        assertEquals(44 + pcm.size, wav.size)

        val bb = ByteBuffer.wrap(wav).order(ByteOrder.LITTLE_ENDIAN)
        val chunk = ByteArray(4)
        bb.get(chunk); assertEquals("RIFF", String(chunk, Charsets.US_ASCII))
        assertEquals(36 + pcm.size, bb.int)                        // ChunkSize
        bb.get(chunk); assertEquals("WAVE", String(chunk, Charsets.US_ASCII))
        bb.get(chunk); assertEquals("fmt ", String(chunk, Charsets.US_ASCII))
        assertEquals(16, bb.int)                                   // Subchunk1Size (PCM)
        assertEquals(1, bb.short.toInt())                          // AudioFormat = PCM
        assertEquals(1, bb.short.toInt())                          // channels = mono
        assertEquals(16_000, bb.int)                               // sampleRate
        assertEquals(16_000 * 2, bb.int)                           // byteRate
        assertEquals(2, bb.short.toInt())                          // blockAlign
        assertEquals(16, bb.short.toInt())                         // bitsPerSample
        bb.get(chunk); assertEquals("data", String(chunk, Charsets.US_ASCII))
        assertEquals(pcm.size, bb.int)                             // Subchunk2Size
    }

    @Test
    fun `concatPcm joins chunks in order with one continuous buffer`() {
        val a = byteArrayOf(1, 2, 3, 4)
        val b = byteArrayOf(5, 6)
        val c = byteArrayOf(7, 8, 9, 10)
        val out = WavUtil.concatPcm(listOf(a, b, c))
        assertEquals(a.size + b.size + c.size, out.size)
        assertArrayEquals(byteArrayOf(1, 2, 3, 4, 5, 6, 7, 8, 9, 10), out)
    }

    @Test
    fun `concatenated turns produce a single header, not one per turn`() {
        // Three short "turns" of raw PCM -> one WAV with exactly one 44-byte header.
        val turns = List(3) { ByteArray(3_200) { i -> ((i + it) % 127).toByte() } }
        val wav = WavUtil.wrapWav(WavUtil.concatPcm(turns))
        assertEquals(44 + 3 * 3_200, wav.size)
        // The only "data" tag is the header's — no embedded WAV headers mid-stream.
        assertEquals(1, countOccurrences(wav, "data".toByteArray(Charsets.US_ASCII)))
        assertEquals(1, countOccurrences(wav, "RIFF".toByteArray(Charsets.US_ASCII)))
    }

    @Test
    fun `cumulative budget scores short turns that never fill one window (BUG-3)`() {
        // 4s + 5s + 4s: no single turn reaches the old 6s gate, but cumulative
        // speech crosses TARGET_SPEECH_SEC by turn 3, so it finalizes.
        val perTurn = listOf(4, 5, 4)
        var cumulative = 0
        var finalizedAt = -1
        perTurn.forEachIndexed { idx, sec ->
            cumulative += sec
            if (finalizedAt < 0 && TurnPolicy.shouldFinalize(cumulative, idx + 1)) finalizedAt = idx + 1
        }
        assertEquals(3, finalizedAt)
        assertFalse(TurnPolicy.isEscapeHatch(cumulative, 3))   // budget met, not an escape
    }

    @Test
    fun `five-turn escape finalizes even when the budget is never met`() {
        // A near-silent speaker: 1s per turn, never reaches TARGET_SPEECH_SEC.
        var cumulative = 0
        var finalizedAt = -1
        for (turn in 1..8) {
            cumulative += 1
            if (finalizedAt < 0 && TurnPolicy.shouldFinalize(cumulative, turn)) finalizedAt = turn
        }
        assertEquals(CaptureParams.MAX_TURNS, finalizedAt)     // escaped at the ceiling
        assertTrue(TurnPolicy.isEscapeHatch(cumulative, CaptureParams.MAX_TURNS))
    }

    private fun countOccurrences(haystack: ByteArray, needle: ByteArray): Int {
        var count = 0
        var i = 0
        outer@ while (i <= haystack.size - needle.size) {
            for (j in needle.indices) if (haystack[i + j] != needle[j]) { i++; continue@outer }
            count++; i += needle.size
        }
        return count
    }
}
