package com.mindsyncvr.core.bluetooth

import com.mindsyncvr.core.model.PpgVitals
import com.mindsyncvr.core.model.RawPpgSample
import kotlin.math.abs
import kotlin.math.pow
import kotlin.math.roundToInt
import kotlin.math.sqrt

class PpgVitalsProcessor {
    fun process(samples: List<RawPpgSample>): PpgVitals {
        val clean = samples
            .distinctBy { it.timestampMs }
            .sortedBy { it.timestampMs }
            .takeLast(MAX_WINDOW_SAMPLES)

        if (clean.size < MIN_SAMPLES) {
            return PpgVitals(status = "Collecting PPG samples")
        }

        val latest = clean.last().timestampMs
        val window = clean.filter { latest - it.timestampMs <= WINDOW_MS }
        if (window.size < MIN_SAMPLES) {
            return PpgVitals(status = "Collecting stable PPG window")
        }

        val durationMs = (window.last().timestampMs - window.first().timestampMs).coerceAtLeast(1L)
        val sampleRateHz = (window.size - 1) * 1000.0 / durationMs.toDouble()
        val values = window.map { it.irValue.toDouble() }
        val mean = values.average()
        val stdDev = sqrt(values.sumOf { (it - mean).pow(2) } / values.size)
        val amplitude = (values.maxOrNull() ?: mean) - (values.minOrNull() ?: mean)
        val signalQuality = estimateSignalQuality(window.size, sampleRateHz, mean, stdDev, amplitude)

        val smoothed = movingAverage(values, SMOOTHING_RADIUS)
        val upward = estimateFromPeaks(window, smoothed)
        val downward = estimateFromPeaks(window, smoothed.map { -it })
        val chosen = listOfNotNull(upward, downward).maxByOrNull { it.score }

        if (chosen == null) {
            return PpgVitals(
                signalQuality = signalQuality,
                confidence = (signalQuality * 0.35).roundToInt(),
                sampleRateHz = sampleRateHz,
                windowSeconds = durationMs / 1000.0,
                status = "Waiting for clear pulse peaks"
            )
        }

        val confidence = (chosen.score * 0.65 + signalQuality * 0.35).roundToInt().coerceIn(0, 100)
        val bpm = chosen.bpm.roundToInt().coerceIn(35, 220)
        return PpgVitals(
            bpm = bpm,
            signalQuality = signalQuality,
            confidence = confidence,
            calmScore = estimateCalmScore(bpm, confidence),
            sampleRateHz = sampleRateHz,
            peakCount = chosen.peakCount,
            windowSeconds = durationMs / 1000.0,
            status = if (confidence >= 60) "Live BPM estimated from raw PPG" else "BPM estimate stabilizing"
        )
    }

    private fun estimateFromPeaks(samples: List<RawPpgSample>, values: List<Double>): PeakEstimate? {
        val mean = values.average()
        val stdDev = sqrt(values.sumOf { (it - mean).pow(2) } / values.size)
        val threshold = mean + stdDev * PEAK_THRESHOLD_STD
        val peaks = mutableListOf<Long>()

        for (index in 1 until values.lastIndex) {
            val timestamp = samples[index].timestampMs
            val isPeak = values[index] > threshold &&
                values[index] > values[index - 1] &&
                values[index] >= values[index + 1]
            val separated = peaks.lastOrNull()?.let { timestamp - it >= MIN_PEAK_DISTANCE_MS } ?: true
            if (isPeak && separated) {
                peaks += timestamp
            }
        }

        if (peaks.size < MIN_PEAK_COUNT) return null

        val intervals = peaks.zipWithNext { first, second -> second - first }
            .filter { it in MIN_RR_MS..MAX_RR_MS }
        if (intervals.size < MIN_INTERVAL_COUNT) return null

        val medianInterval = intervals.sorted()[intervals.size / 2].toDouble()
        val bpm = 60_000.0 / medianInterval
        if (bpm !in MIN_BPM..MAX_BPM) return null

        val intervalMean = intervals.average()
        val intervalStd = sqrt(intervals.sumOf { (it - intervalMean).pow(2) } / intervals.size)
        val regularity = (100.0 - (intervalStd / intervalMean * 140.0)).coerceIn(0.0, 100.0)
        val peakSupport = (intervals.size * 18.0).coerceAtMost(100.0)
        return PeakEstimate(
            bpm = bpm,
            peakCount = peaks.size,
            score = (regularity * 0.62 + peakSupport * 0.38).coerceIn(0.0, 100.0)
        )
    }

    private fun movingAverage(values: List<Double>, radius: Int): List<Double> {
        return values.mapIndexed { index, _ ->
            val start = (index - radius).coerceAtLeast(0)
            val end = (index + radius).coerceAtMost(values.lastIndex)
            values.subList(start, end + 1).average()
        }
    }

    private fun estimateSignalQuality(
        sampleCount: Int,
        sampleRateHz: Double,
        mean: Double,
        stdDev: Double,
        amplitude: Double
    ): Int {
        val sampleScore = (sampleCount / 140.0 * 100.0).coerceIn(0.0, 100.0)
        val rateScore = when {
            sampleRateHz < 10.0 -> sampleRateHz / 10.0 * 45.0
            sampleRateHz <= 120.0 -> 100.0
            else -> 75.0
        }
        val acRatio = if (mean == 0.0) 0.0 else stdDev / mean
        val acScore = (acRatio / 0.006 * 100.0).coerceIn(0.0, 100.0)
        val amplitudeScore = (amplitude / 1200.0 * 100.0).coerceIn(0.0, 100.0)
        return (sampleScore * 0.25 + rateScore * 0.25 + acScore * 0.25 + amplitudeScore * 0.25)
            .roundToInt()
            .coerceIn(0, 100)
    }

    private fun estimateCalmScore(bpm: Int, confidence: Int): Int {
        val bpmScore = (100 - abs(bpm - CALM_REFERENCE_BPM) * 1.35).coerceIn(0.0, 100.0)
        return (bpmScore * 0.72 + confidence * 0.28).roundToInt().coerceIn(0, 100)
    }

    private data class PeakEstimate(
        val bpm: Double,
        val peakCount: Int,
        val score: Double
    )

    private companion object {
        const val WINDOW_MS = 15_000L
        const val MAX_WINDOW_SAMPLES = 900
        const val MIN_SAMPLES = 80
        const val SMOOTHING_RADIUS = 2
        const val PEAK_THRESHOLD_STD = 0.35
        const val MIN_PEAK_DISTANCE_MS = 320L
        const val MIN_RR_MS = 330L
        const val MAX_RR_MS = 1_500L
        const val MIN_PEAK_COUNT = 3
        const val MIN_INTERVAL_COUNT = 2
        const val MIN_BPM = 40.0
        const val MAX_BPM = 180.0
        const val CALM_REFERENCE_BPM = 68.0
    }
}
