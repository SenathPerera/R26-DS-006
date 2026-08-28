package com.mindsyncvr.core.bluetooth

import android.util.Log
import com.mindsyncvr.core.model.RawPpgSample
import java.nio.ByteBuffer
import java.nio.ByteOrder

object PpgPacketParser {
    private const val TAG = "PpgPacketParser"
    const val MAX_SAMPLES_PER_PACKET = 5
    const val SAMPLE_SIZE_BYTES = 8
    const val RAW_BATCH_PACKET_SIZE_BYTES = 1 + MAX_SAMPLES_PER_PACKET * SAMPLE_SIZE_BYTES

    fun parseRawBatchPacket(payload: ByteArray): Result<List<RawPpgSample>> {
        if (payload.size != RAW_BATCH_PACKET_SIZE_BYTES) {
            val message = "Malformed PPG packet: expected $RAW_BATCH_PACKET_SIZE_BYTES bytes, got ${payload.size}"
            Log.w(TAG, message)
            return Result.failure(IllegalArgumentException(message))
        }

        val sampleCount = payload[0].toInt() and 0xFF
        if (sampleCount > MAX_SAMPLES_PER_PACKET) {
            val message = "Malformed PPG packet: sample_count=$sampleCount exceeds $MAX_SAMPLES_PER_PACKET"
            Log.w(TAG, message)
            return Result.failure(IllegalArgumentException(message))
        }

        val buffer = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        buffer.position(1)

        val samples = buildList(sampleCount) {
            repeat(MAX_SAMPLES_PER_PACKET) { index ->
                val timestampMs = buffer.int.toUInt().toLong()
                val irValue = buffer.int.toUInt().toLong()
                if (index < sampleCount) {
                    add(RawPpgSample(timestampMs = timestampMs, irValue = irValue))
                }
            }
        }

        Log.d(TAG, "Decoded PPG packet sample_count=$sampleCount latest=${samples.lastOrNull()}")
        return Result.success(samples)
    }
}
