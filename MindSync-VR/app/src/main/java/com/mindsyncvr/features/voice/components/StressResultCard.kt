package com.mindsyncvr.features.voice.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.voice.AudioMetrics
import com.mindsyncvr.core.voice.VoiceAnalysis
import java.util.Locale
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

/**
 * WP4 — the full voice-stress reading, ported from the web client's `StressCard`
 * + `classifyReasons` (component-d/clients/web/src/ui.jsx). Every /infer result,
 * pre and post, shows the same depth: score + level, a meter, the low-confidence
 * deferral banner (my actual research finding), the metrics grid (valence,
 * arousal, confidence, type, duration, speech %, loudness) and, optionally, the
 * plain-language "Why this reading" list.
 *
 * Mirrors the web LOW_CONF = 0.4 gate: below it, the score reads "uncertain"
 * (muted) rather than a confident level, and the deferral banner appears.
 */
private const val LOW_CONF = 0.4

private fun levelFill(level: String): Color = when (level.lowercase()) {
    "no" -> Green
    "mild" -> Cyan
    "moderate" -> Amber
    "high" -> Rose
    else -> TextMuted
}

/** Plain-language stress-TYPE label (arousal names the type, not the magnitude). */
fun stressTypeLabel(t: String?): String? = when (t) {
    "activated" -> "Activated · fight-or-flight"
    "shutdown" -> "Withdrawn · freeze"
    else -> null
}

fun stressTypeShort(t: String?): String? = when (t) {
    "activated" -> "activated"
    "shutdown" -> "withdrawn"
    else -> null
}

/** Ported line-for-line from web `classifyReasons()`. */
fun classifyReasons(a: VoiceAnalysis): List<String> {
    val out = mutableListOf<String>()
    val vSign = if (a.valence >= 0) "+" else ""
    val vTone = when {
        a.valence < -0.15 -> "unpleasant"
        a.valence > 0.15 -> "pleasant"
        else -> "neutral"
    }
    out += "Valence $vSign${fmt2(a.valence)} — the tone reads as $vTone."
    val aSign = if (a.arousal >= 0) "+" else ""
    val energy = when {
        a.arousal >= 0.15 -> "activated / keyed-up"
        a.arousal <= -0.15 -> "subdued / low"
        else -> "level"
    }
    out += "Arousal $aSign${fmt2(a.arousal)} — energy is $energy."
    when (a.stressType) {
        "shutdown" -> out += "Negative tone with LOW energy → \"withdrawn (freeze)\" stress — quiet, internalised tension."
        "activated" -> out += "Negative tone with HIGH energy → \"activated (fight-or-flight)\" stress — agitated, keyed-up."
    }
    val confMeaning = when {
        a.confidence >= 0.7 -> "a clear, well-separated reading"
        a.confidence >= 0.4 -> "a moderate reading"
        else -> "a faint reading near neutral, treat with care"
    }
    out += "Confidence ${fmt2(a.confidence)} — $confMeaning."
    return out
}

@Composable
fun StressResultCard(result: VoiceAnalysis, showReasons: Boolean = true) {
    val lowConf = result.confidence < LOW_CONF
    val fill = if (lowConf) TextMuted else levelFill(result.stressLevel)
    GlassCard {
        // score + level
        Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(fmt2(result.stressScore), color = fill, fontSize = 34.sp, fontWeight = FontWeight.Bold)
            Text("/ 10", color = TextMuted, fontSize = 15.sp, modifier = Modifier.padding(bottom = 6.dp))
            Spacer(Modifier.weight(1f))
            val label = if (lowConf) "uncertain"
            else result.stressLevel + (stressTypeShort(result.stressType)?.let { " · $it" } ?: "")
            StatusPill(label.replaceFirstChar { it.uppercase() }, fill)
        }
        // meter
        val pct = max(3.0, min(100.0, result.stressScore * 10)).toFloat() / 100f
        Box(Modifier.fillMaxWidth().height(8.dp).background(SurfaceGlass, CircleShape)) {
            Box(Modifier.fillMaxWidth(pct).height(8.dp).background(fill, CircleShape))
        }
        // low-confidence deferral banner
        if (lowConf) {
            Column(
                Modifier.fillMaxWidth()
                    .background(SurfaceGlass, RoundedCornerShape(14.dp))
                    .border(1.dp, Color(0x29CCE7FF), RoundedCornerShape(14.dp))
                    .padding(12.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                Text("Low-confidence voice reading (≈${fmt2(result.confidence)}).",
                    color = TextPrimary, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                Text("This voice sits near neutral, so the score is uncertain — not a confident calm. " +
                    "The system treats voice as unreliable here and defers to the heart-rate signal (Layer 4). " +
                    "This is expected for out-of-distribution voices such as Sinhala.",
                    color = TextMuted, fontSize = 12.sp, lineHeight = 17.sp)
            }
        }
        // metrics grid
        val q: AudioMetrics? = result.quality
        MetricGrid(buildList {
            add("Valence" to signed3(result.valence))
            add("Arousal" to signed3(result.arousal))
            add("Confidence" to fmt3(result.confidence))
            add("Level" to result.stressLevel.replaceFirstChar { it.uppercase() })
            add("Type" to (stressTypeLabel(result.stressType) ?: "—"))
            if (q != null) add("Duration" to "${fmt1(q.durationSec)}s")
            if (q != null) add("Speech" to "${(q.speechFraction * 100).toInt()}%")
            if (q != null) add("Loudness" to fmt3(q.rms))
        })
        if (showReasons) {
            Text("◆ Why this reading", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            classifyReasons(result).forEach {
                Text("• $it", color = TextMuted, fontSize = 12.sp, lineHeight = 18.sp)
            }
        }
    }
}

@Composable
private fun MetricGrid(cells: List<Pair<String, String>>) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        cells.chunked(2).forEach { row ->
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                row.forEach { (k, v) -> MetricCell(k, v, Modifier.weight(1f)) }
                if (row.size == 1) Spacer(Modifier.weight(1f))
            }
        }
    }
}

@Composable
private fun MetricCell(key: String, value: String, modifier: Modifier = Modifier) {
    Column(
        modifier.background(Color(0x14FFFFFF), RoundedCornerShape(12.dp)).padding(10.dp),
        verticalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        Text(key, color = TextMuted, fontSize = 10.sp, fontWeight = FontWeight.SemiBold)
        Text(value, color = TextPrimary, fontSize = 14.sp, fontWeight = FontWeight.Bold)
    }
}

private fun fmt1(d: Double) = String.format(Locale.US, "%.1f", d)
private fun fmt2(d: Double) = String.format(Locale.US, "%.2f", d)
private fun fmt3(d: Double) = String.format(Locale.US, "%.3f", d)
private fun signed3(d: Double) = (if (d >= 0) "+" else "") + fmt3(d)

// ------------------------------------------------------------------ previews

private val previewHigh = VoiceAnalysis(
    sessionId = "p", stressScore = 7.8, stressLevel = "high", stressType = "activated",
    confidence = 0.82, valence = -0.78, arousal = 0.34, gateMean = 0.5,
    quality = AudioMetrics(12.3, 0.031, 0.0, 9.4, 0.78, 4),
    body = null, inputLevel = null, warnings = emptyList(),
)

private val previewLowConf = previewHigh.copy(
    stressScore = 4.1, stressLevel = "mild", stressType = null, confidence = 0.28, valence = -0.09,
)

@Preview(name = "Confident high", backgroundColor = 0xFF07111F, showBackground = true, widthDp = 360)
@Composable
private fun PreviewHigh() { StressResultCard(previewHigh) }

@Preview(name = "Low confidence", backgroundColor = 0xFF07111F, showBackground = true, widthDp = 360)
@Composable
private fun PreviewLowConf() { StressResultCard(previewLowConf) }
