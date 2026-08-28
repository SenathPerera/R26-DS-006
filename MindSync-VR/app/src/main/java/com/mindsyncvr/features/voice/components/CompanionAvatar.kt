package com.mindsyncvr.features.voice.components

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import com.mindsyncvr.core.design.Cyan
import com.mindsyncvr.core.design.Indigo
import com.mindsyncvr.core.design.Midnight
import com.mindsyncvr.core.design.Teal
import com.mindsyncvr.core.design.TextPrimary
import com.mindsyncvr.core.design.Violet
import kotlin.math.cos
import kotlin.math.sin

/**
 * The AI health companion's on-screen presence (WP9) — an original geometric
 * "agent", drawn entirely in one Compose [Canvas] (no bitmap, no Lottie, no
 * mascot). The old version read as a flat pulsing shape; this one is built to
 * read as *something that is looking at you and talking back*:
 *
 *  - A layered body: a soft outer aura, an inner glow, an iris-like core, and a
 *    bright **pupil** that slowly drifts as though orienting toward the person —
 *    the focal point a plain circle lacks.
 *  - Idle micro-motion: a ~4s breathing scale plus a gentle drift, so it never
 *    looks paused.
 *  - Four states, readable at a glance and animated (not snapped) between:
 *      [Idle]      dim core, rings barely moving, slow breath
 *      [Listening] outer ring tracks the live mic [amplitude]; core brightens;
 *                  a ring is drawn pulling INWARD (drawing the voice in)
 *      [Speaking]  concentric pulses radiate OUTWARD — deliberately the visual
 *                  opposite of listening, since the two alternate constantly
 *      [Thinking]  a calm arc travels around the core (never a spinner)
 *
 * State changes cross-fade because the per-state visual weights are driven by
 * [animateFloatAsState] (~420ms). If the system animator scale is 0, those
 * settle instantly to a valid static per-state appearance and the infinite
 * transitions hold — the avatar degrades rather than freezing mid-motion.
 * Colours are theme-only (Teal/Cyan/Violet/Indigo/Midnight).
 */
enum class AvatarState { Idle, Speaking, Listening, Thinking }

private const val TAU = (2 * Math.PI).toFloat()

@Composable
fun CompanionAvatar(
    state: AvatarState,
    modifier: Modifier = Modifier,
    amplitude: Float = 0f,   // 0..1 live mic level, used by [AvatarState.Listening]
    size: Int = 160,
) {
    val t = rememberInfiniteTransition(label = "avatar")
    // Continuous phases (each state uses the ones it needs).
    val breath by t.animateFloat(
        0.96f, 1.05f, infiniteRepeatable(tween(4000), RepeatMode.Reverse), label = "breath")
    val drift by t.animateFloat(
        0f, TAU, infiniteRepeatable(tween(9000, easing = LinearEasing), RepeatMode.Restart), label = "drift")
    val sweep by t.animateFloat(
        0f, TAU, infiniteRepeatable(tween(2600, easing = LinearEasing), RepeatMode.Restart), label = "sweep")
    val pulse by t.animateFloat(
        0f, 1f, infiniteRepeatable(tween(1900, easing = LinearEasing), RepeatMode.Restart), label = "pulse")

    // Per-state visual weights — animating these is what makes the companion
    // "shift attention" rather than teleport between modes.
    val listenW by animateFloatAsState(if (state == AvatarState.Listening) 1f else 0f, tween(420), label = "listenW")
    val speakW by animateFloatAsState(if (state == AvatarState.Speaking) 1f else 0f, tween(420), label = "speakW")
    val thinkW by animateFloatAsState(if (state == AvatarState.Thinking) 1f else 0f, tween(420), label = "thinkW")
    val coreGlow by animateFloatAsState(
        when (state) {
            AvatarState.Listening -> 1f
            AvatarState.Speaking -> 0.85f
            AvatarState.Thinking -> 0.6f
            AvatarState.Idle -> 0.45f
        }, tween(420), label = "coreGlow")

    Box(modifier.size(size.dp), contentAlignment = Alignment.Center) {
        Canvas(Modifier.size(size.dp)) {
            val r = this.size.minDimension / 2f
            // Idle drift: a subtle wander so it feels alive at rest.
            val driftAmt = r * 0.03f
            val c = Offset(
                this.size.width / 2f + cos(drift) * driftAmt,
                this.size.height / 2f + sin(drift * 0.8f) * driftAmt,
            )
            val scale = 0.86f * (0.98f + 0.02f * breath) + amplitude * listenW * 0.10f

            // 1) Outer aura — soft, breathing, brightens a touch while listening.
            drawCircle(
                brush = Brush.radialGradient(
                    listOf(Teal.copy(alpha = 0.16f + 0.18f * listenW + 0.10f * speakW), Color.Transparent),
                    center = c, radius = r,
                ),
                radius = r * (0.96f * breath), center = c,
            )

            // 2) Speaking — concentric pulses radiating OUTWARD (3 staggered rings).
            if (speakW > 0.01f) {
                for (k in 0 until 3) {
                    val p = (pulse + k / 3f) % 1f
                    drawCircle(
                        color = Cyan.copy(alpha = (1f - p) * 0.5f * speakW),
                        radius = r * (0.4f + p * 0.58f), center = c,
                        style = Stroke(width = r * 0.035f),
                    )
                }
            }

            // 3) Listening — outer ring tracks the mic, plus a ring pulling INWARD.
            if (listenW > 0.01f) {
                drawCircle(
                    color = Cyan.copy(alpha = (0.30f + amplitude * 0.5f) * listenW),
                    radius = r * (0.74f + amplitude * 0.22f), center = c,
                    style = Stroke(width = r * 0.05f),
                )
                val inward = 1f - (pulse)                     // large -> small (drawn in)
                drawCircle(
                    color = Teal.copy(alpha = pulse * 0.35f * listenW),
                    radius = r * (0.44f + inward * 0.44f), center = c,
                    style = Stroke(width = r * 0.03f),
                )
            }

            // 4) Body — inner glow + iris core (layered depth).
            val bodyR = r * 0.52f * scale
            drawCircle(
                brush = Brush.radialGradient(
                    listOf(Violet.copy(alpha = 0.55f), Indigo.copy(alpha = 0.32f), Color.Transparent),
                    center = c, radius = bodyR * 1.5f,
                ),
                radius = bodyR * 1.5f, center = c,
            )
            drawCircle(
                brush = Brush.radialGradient(
                    listOf(Indigo.copy(alpha = 0.9f), Violet.copy(alpha = 0.5f)),
                    center = c, radius = bodyR,
                ),
                radius = bodyR, center = c,
            )
            drawCircle(Cyan.copy(alpha = 0.85f), radius = bodyR, center = c, style = Stroke(width = r * 0.025f))

            // 5) Thinking — a bright arc travelling around the iris.
            if (thinkW > 0.01f) {
                drawArc(
                    color = Cyan.copy(alpha = 0.9f * thinkW),
                    startAngle = Math.toDegrees(sweep.toDouble()).toFloat(),
                    sweepAngle = 70f, useCenter = false,
                    topLeft = Offset(c.x - bodyR * 1.28f, c.y - bodyR * 1.28f),
                    size = Size(bodyR * 2.56f, bodyR * 2.56f),
                    style = Stroke(width = r * 0.045f),
                )
            }

            // 6) The focal point — an iris ring and a bright pupil that slowly
            //    orients toward the person (drift), so it reads as *looking*.
            val irisR = bodyR * 0.62f
            drawCircle(
                brush = Brush.radialGradient(
                    listOf(Teal.copy(alpha = 0.35f + 0.45f * coreGlow), Color.Transparent),
                    center = c, radius = irisR,
                ),
                radius = irisR, center = c,
            )
            val look = Offset(
                c.x + cos(drift) * irisR * 0.28f * (0.4f + listenW),
                c.y + sin(drift * 0.8f) * irisR * 0.22f * (0.4f + listenW),
            )
            drawCircle(TextPrimary.copy(alpha = 0.4f + 0.6f * coreGlow), radius = irisR * 0.42f, center = look)
            drawCircle(Cyan.copy(alpha = 0.9f * coreGlow), radius = irisR * 0.16f, center = look)
        }
    }
}

// ------------------------------------------------------------------ previews

@Preview(name = "Idle", widthDp = 200, heightDp = 200, backgroundColor = 0xFF07111F, showBackground = true)
@Composable
private fun PreviewIdle() { PreviewBox(AvatarState.Idle) }

@Preview(name = "Listening", widthDp = 200, heightDp = 200, backgroundColor = 0xFF07111F, showBackground = true)
@Composable
private fun PreviewListening() { PreviewBox(AvatarState.Listening, amplitude = 0.7f) }

@Preview(name = "Speaking", widthDp = 200, heightDp = 200, backgroundColor = 0xFF07111F, showBackground = true)
@Composable
private fun PreviewSpeaking() { PreviewBox(AvatarState.Speaking) }

@Preview(name = "Thinking", widthDp = 200, heightDp = 200, backgroundColor = 0xFF07111F, showBackground = true)
@Composable
private fun PreviewThinking() { PreviewBox(AvatarState.Thinking) }

@Preview(name = "Idle small", widthDp = 90, heightDp = 90, backgroundColor = 0xFF07111F, showBackground = true)
@Composable
private fun PreviewSmall() { PreviewBox(AvatarState.Idle, size = 72) }

@Composable
private fun PreviewBox(state: AvatarState, amplitude: Float = 0f, size: Int = 160) {
    Box(Modifier.size((size + 20).dp), contentAlignment = Alignment.Center) {
        Canvas(Modifier.size((size + 20).dp)) { drawRect(Midnight, size = Size(this.size.width, this.size.height)) }
        CompanionAvatar(state = state, amplitude = amplitude, size = size)
    }
}
