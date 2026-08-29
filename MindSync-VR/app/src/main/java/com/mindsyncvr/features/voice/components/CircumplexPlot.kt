package com.mindsyncvr.features.voice.components

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.voice.AudioMetrics
import com.mindsyncvr.core.voice.VoiceAnalysis
import java.util.Locale
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin

/**
 * WP5 — the valence/arousal circumplex, ported from the web `Circumplex`
 * (ui.jsx). Reads left→right as the stress axis (valence): the LEFT half
 * (unpleasant) is the stress side and is shaded. Vertical is arousal, which only
 * names the TYPE of stress. The dashed arrow from "before" → "after" is the
 * story of the session — the movement is the primary signal.
 */
private data class VaPoint(val valence: Double, val arousal: Double, val label: String, val color: Color)

@Composable
fun CircumplexPlot(pre: VoiceAnalysis?, post: VoiceAnalysis?, modifier: Modifier = Modifier) {
    val points = buildList {
        pre?.let { add(VaPoint(it.valence, it.arousal, "before", Rose)) }
        post?.let { add(VaPoint(it.valence, it.arousal, "after", Green)) }
    }
    val tm = rememberTextMeasurer()
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Canvas(
            Modifier.fillMaxWidth().aspectRatio(1f)
                .clip(androidx.compose.foundation.shape.RoundedCornerShape(16.dp))
                .background(Color(0x14FFFFFF)),
        ) {
            val pad = size.minDimension * 0.12f
            val box = size.minDimension - pad * 2
            val cx = pad + box / 2f
            val cy = pad + box / 2f
            val s = box / 2f
            fun px(v: Double) = cx + v.coerceIn(-1.0, 1.0).toFloat() * s
            fun py(a: Double) = cy - a.coerceIn(-1.0, 1.0).toFloat() * s

            // shaded stress (negative-valence) half
            drawRect(Rose.copy(alpha = 0.07f), topLeft = Offset(pad, pad), size = Size(box / 2f, box))
            // axes
            drawLine(Color(0x33CCE7FF), Offset(cx, pad), Offset(cx, pad + box), strokeWidth = 1.dp.toPx())
            drawLine(Color(0x33CCE7FF), Offset(pad, cy), Offset(pad + box, cy), strokeWidth = 1.dp.toPx())

            val cap = TextStyle(color = TextMuted, fontSize = 8.sp)
            fun label(text: String, x: Float, y: Float, style: TextStyle = cap) {
                val m = tm.measure(text, style)
                drawText(m, topLeft = Offset(x, y))
            }
            // quadrant labels (plain language)
            label("▲ fight-or-flight", pad + 4f, pad + 4f, TextStyle(color = Rose, fontSize = 8.sp, fontWeight = FontWeight.Bold))
            label("▼ freeze", pad + 4f, pad + box - 18f, TextStyle(color = Amber, fontSize = 8.sp, fontWeight = FontWeight.Bold))
            label("relaxed", pad + box - 44f, pad + box - 18f, TextStyle(color = Green, fontSize = 8.sp))
            label("excited", pad + box - 44f, pad + 4f, cap)
            // axis captions
            label("← unpleasant · stress", pad, pad + box + 4f)
            label("pleasant →", pad + box - 56f, pad + box + 4f)

            // before → after dashed arrow
            if (points.size == 2) {
                val a = Offset(px(points[0].valence), py(points[0].arousal))
                val b = Offset(px(points[1].valence), py(points[1].arousal))
                drawLine(Violet, a, b, strokeWidth = 2.dp.toPx(),
                    pathEffect = PathEffect.dashPathEffect(floatArrayOf(10f, 8f)))
                drawArrowHead(a, b, Violet)
            }
            points.forEachIndexed { i, p ->
                val at = Offset(px(p.valence), py(p.arousal))
                if (i == 0) drawCircle(p.color.copy(alpha = 0.4f), radius = 15f, center = at, style = Stroke(width = 1.5.dp.toPx()))
                drawCircle(p.color, radius = 9f, center = at)
                label(p.label, at.x - 14f, at.y - 26f, TextStyle(color = p.color, fontSize = 9.sp, fontWeight = FontWeight.Bold))
            }
        }
        // legend
        points.forEach { p ->
            val src = if (p.label == "before") pre else post
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Box(Modifier.size(10.dp).background(p.color, CircleShape))
                Column {
                    Text(p.label.replaceFirstChar { it.uppercase() } +
                        (stressTypeLabel(src?.stressType)?.let { " — $it" } ?: ""),
                        color = p.color, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                    if (src != null) Text(
                        "valence ${f2(src.valence)}, arousal ${f2(src.arousal)}, confidence ${f2(src.confidence)}",
                        color = TextMuted, fontSize = 11.sp)
                }
            }
        }
    }
}

private fun DrawScope.drawArrowHead(from: Offset, to: Offset, color: Color) {
    val ang = atan2((to.y - from.y).toDouble(), (to.x - from.x).toDouble())
    val len = 14f
    rotate(Math.toDegrees(ang).toFloat(), pivot = to) {
        drawLine(color, to, Offset(to.x - len, to.y - len * 0.5f), strokeWidth = 2.dp.toPx())
        drawLine(color, to, Offset(to.x - len, to.y + len * 0.5f), strokeWidth = 2.dp.toPx())
    }
}

private fun f2(d: Double) = String.format(Locale.US, "%.2f", d)

// ------------------------------------------------------------------ previews

private fun pt(v: Double, a: Double, conf: Double = 0.8, type: String? = null) = VoiceAnalysis(
    "p", 5.0, "moderate", type, conf, v, a, 0.5,
    AudioMetrics(10.0, 0.03, 0.0, 8.0, 0.8, 3), null, null, emptyList(),
)

@Preview(name = "Improved", backgroundColor = 0xFF07111F, showBackground = true, widthDp = 320)
@Composable
private fun PreviewImproved() { CircumplexPlot(pt(-0.7, 0.4, type = "activated"), pt(0.4, -0.1)) }

@Preview(name = "Worsened", backgroundColor = 0xFF07111F, showBackground = true, widthDp = 320)
@Composable
private fun PreviewWorsened() { CircumplexPlot(pt(-0.2, 0.1), pt(-0.7, 0.5, type = "activated")) }

@Preview(name = "Little change", backgroundColor = 0xFF07111F, showBackground = true, widthDp = 320)
@Composable
private fun PreviewSteady() { CircumplexPlot(pt(-0.3, 0.2), pt(-0.28, 0.15)) }
