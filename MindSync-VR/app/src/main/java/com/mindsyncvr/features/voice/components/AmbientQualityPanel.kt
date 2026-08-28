package com.mindsyncvr.features.voice.components

import androidx.compose.foundation.background
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
import com.mindsyncvr.core.voice.AmbientCheck
import com.mindsyncvr.core.voice.AmbientResult
import com.mindsyncvr.core.voice.AudioMetrics
import java.util.Locale

/**
 * WP1 (mobile) — the Layer-1 room-quality panel, the native equivalent of the
 * web `QualityBar` but richer: a 0–100 score ring, the noise TYPE named plainly,
 * and every check from the server's `checks[]` as its own pass/fail row with the
 * human message. This replaces the old single-bar RoomQualityCard.
 */
private fun noiseTypePlain(t: String): String = when (t) {
    "hum" -> "a steady hum (fan or AC)"
    "broadband" -> "background noise (traffic or outside)"
    "hiss" -> "an electrical hiss"
    "intermittent" -> "on-and-off sounds"
    "voices" -> "nearby voices"
    else -> "quiet"
}

/** Concrete suggestion keyed to the noise type (mirrors WP1's companion lines). */
fun ambientSuggestion(noiseType: String): String = when (noiseType) {
    "hum" -> "Could you move away from the fan or air conditioning, or switch it off for a minute?"
    "broadband" -> "Could you close the window, or move away from the road?"
    "voices" -> "I can hear someone talking nearby — somewhere more private would help."
    "intermittent" -> "There's some movement around you — let's wait for it to settle."
    "hiss" -> "There's an electrical hiss — moving away from the desk or charger might help."
    else -> "Let's find a quieter moment and try once more."
}

@Composable
fun AmbientQualityPanel(a: AmbientResult, bestScore: Int? = null, attempts: Int = 0) {
    val tone = if (a.ok) Green else Amber
    GlassCard {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Column {
                Text("Room quality", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                Text(if (a.ok) "Quiet enough to record" else "It's ${noiseTypePlain(a.noiseType)}",
                    color = TextPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
            }
            ProgressRing(a.score, "/100")
        }

        if (!a.ok) {
            Column(
                Modifier.fillMaxWidth().background(Amber.copy(alpha = 0.10f), RoundedCornerShape(14.dp)).padding(12.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                Text(ambientSuggestion(a.noiseType), color = TextPrimary, fontSize = 13.sp, lineHeight = 18.sp)
                if (attempts >= 3 && bestScore != null) {
                    Text("Best so far: $bestScore/100 over $attempts tries — move around and watch the score.",
                        color = TextMuted, fontSize = 11.sp)
                }
            }
        }

        a.checks.forEach { CheckRow(it) }
    }
}

@Composable
private fun CheckRow(c: AmbientCheck) {
    val ok = c.pass
    val tone = if (ok) Green else if (c.severity == "warn") Amber else Rose
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
        Box(Modifier.size(20.dp).background(tone.copy(alpha = 0.18f), CircleShape), contentAlignment = Alignment.Center) {
            Text(if (ok) "✓" else if (c.severity == "warn") "!" else "✕", color = tone, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        }
        Column(Modifier.weight(1f)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                Text(c.label, color = TextPrimary, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
                Text(valueLabel(c), color = tone, fontSize = 12.sp, fontWeight = FontWeight.Bold)
            }
            if (!ok) Text(c.message, color = TextMuted, fontSize = 11.sp, lineHeight = 16.sp)
        }
    }
}

private fun valueLabel(c: AmbientCheck): String {
    val v = when (c.unit) {
        "dBFS" -> String.format(Locale.US, "%.0f dBFS", c.value)
        "s" -> String.format(Locale.US, "%.1fs", c.value)
        "ratio" -> String.format(Locale.US, "%.0f%%", c.value * 100)
        else -> String.format(Locale.US, "%.2f", c.value)
    }
    return v
}

// ------------------------------------------------------------------ previews

private val SM = AudioMetrics(8.0, 0.01, 0.0, 0.0, 0.0, 0)

@Preview(name = "Quiet pass", backgroundColor = 0xFF07111F, showBackground = true, widthDp = 360)
@Composable
private fun PreviewPass() {
    AmbientQualityPanel(AmbientResult(
        ok = true, reasons = emptyList(), metrics = SM, score = 88, noiseType = "quiet",
        checks = listOf(
            AmbientCheck("noise_floor", "Background noise", -54.0, "dBFS", true, "fail", "quiet"),
            AmbientCheck("peaks", "Sudden sounds", -46.0, "dBFS", true, "fail", "ok"),
            AmbientCheck("voices", "Nearby speech", 0.0, "s", true, "fail", "ok"),
        ),
    ))
}

@Preview(name = "Hum fail", backgroundColor = 0xFF07111F, showBackground = true, widthDp = 360)
@Composable
private fun PreviewHum() {
    AmbientQualityPanel(AmbientResult(
        ok = false, reasons = listOf("too_noisy"), metrics = SM, score = 42, noiseType = "hum",
        checks = listOf(
            AmbientCheck("noise_floor", "Background noise", -41.0, "dBFS", false, "fail", "There's a steady background sound — it may be a fan or air conditioning."),
            AmbientCheck("peaks", "Sudden sounds", -40.0, "dBFS", true, "fail", "ok"),
            AmbientCheck("tonal_noise", "Hum", 0.72, "ratio", false, "warn", "There's a steady low hum — it may be a fan or air conditioning."),
        ),
    ), bestScore = 48, attempts = 3)
}
