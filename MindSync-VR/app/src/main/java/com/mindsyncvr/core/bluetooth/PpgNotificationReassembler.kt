package com.mindsyncvr.core.bluetooth

import com.mindsyncvr.core.model.RawPpgSample
import java.io.ByteArrayOutputStream

class PpgNotificationReassembler {
    private val buffer = ByteArrayOutputStream()

    fun append(chunk: ByteArray): ReassemblyResult {
        if (chunk.isEmpty()) return ReassemblyResult(emptyList(), null, buffer.size())

        buffer.write(chunk)
        val decoded = mutableListOf<RawPpgSample>()
        var warning: String? = null

        while (buffer.size() >= PpgPacketParser.RAW_BATCH_PACKET_SIZE_BYTES) {
            val bytes = buffer.toByteArray()
            val packet = bytes.copyOfRange(0, PpgPacketParser.RAW_BATCH_PACKET_SIZE_BYTES)
            val remaining = bytes.copyOfRange(PpgPacketParser.RAW_BATCH_PACKET_SIZE_BYTES, bytes.size)

            PpgPacketParser.parseRawBatchPacket(packet)
                .onSuccess { decoded += it }
                .onFailure { warning = it.message }

            buffer.reset()
            buffer.write(remaining)
        }

        if (buffer.size() > MAX_BUFFER_BYTES) {
            warning = "PPG reassembly buffer overflow: ${buffer.size()} bytes; dropping partial packet"
            buffer.reset()
        }

        return ReassemblyResult(
            samples = decoded,
            warning = warning,
            bufferedBytes = buffer.size()
        )
    }

    fun reset() {
        buffer.reset()
    }

    data class ReassemblyResult(
        val samples: List<RawPpgSample>,
        val warning: String?,
        val bufferedBytes: Int
    )

    private companion object {
        const val MAX_BUFFER_BYTES = PpgPacketParser.RAW_BATCH_PACKET_SIZE_BYTES * 4
    }
}
