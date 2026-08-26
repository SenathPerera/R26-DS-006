package com.mindsyncvr.core.design

import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable
fun MindSyncScaffold(content: @Composable ColumnScope.() -> Unit) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(
                Brush.verticalGradient(
                    listOf(Midnight, Color(0xFF0B1B32), Color(0xFF171A3E))
                )
            )
            .systemBarsPadding()
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 22.dp, vertical = 20.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp),
            content = content
        )
    }
}

@Composable
fun GlassCard(
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(
                Brush.verticalGradient(listOf(Color(0x26FFFFFF), Color(0x0FFFFFFF))),
                RoundedCornerShape(22.dp)
            )
            .border(1.dp, Color(0x2ECCE7FF), RoundedCornerShape(22.dp))
            .padding(18.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        content = content
    )
}

@Composable
fun PrimaryButton(text: String, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        modifier = modifier.fillMaxWidth().height(56.dp),
        shape = RoundedCornerShape(20.dp),
        colors = ButtonDefaults.buttonColors(containerColor = Color.Transparent),
        contentPadding = PaddingValues()
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Brush.horizontalGradient(listOf(Cyan, Violet, Teal))),
            contentAlignment = Alignment.Center
        ) {
            Text(text, color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 16.sp)
        }
    }
}

@Composable
fun SecondaryButton(text: String, modifier: Modifier = Modifier, danger: Boolean = false, onClick: () -> Unit) {
    OutlinedButton(
        onClick = onClick,
        modifier = modifier.fillMaxWidth().height(54.dp),
        shape = RoundedCornerShape(18.dp),
        colors = ButtonDefaults.outlinedButtonColors(
            contentColor = if (danger) Danger else TextPrimary,
            containerColor = if (danger) Color(0x1FFF7A8A) else Color(0x1FFFFFFF)
        ),
        border = BorderStroke(1.dp, if (danger) Danger.copy(alpha = 0.45f) else Color(0x29CCE7FF))
    ) {
        Text(text, fontWeight = FontWeight.Bold)
    }
}

@Composable
fun SectionHeader(title: String, subtitle: String? = null) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(title, color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 28.sp, lineHeight = 34.sp)
        if (subtitle != null) Text(subtitle, color = TextMuted, fontSize = 15.sp, lineHeight = 22.sp)
    }
}

@Composable
fun StatusPill(text: String, tone: Color = Cyan) {
    Row(
        modifier = Modifier
            .background(tone.copy(alpha = 0.12f), CircleShape)
            .border(1.dp, tone.copy(alpha = 0.35f), CircleShape)
            .padding(horizontal = 12.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(Modifier.size(7.dp).background(tone, CircleShape))
        Text(text, color = tone, fontSize = 11.sp, fontWeight = FontWeight.Bold)
    }
}

@Composable
fun OptionChip(label: String, selected: Boolean, onClick: () -> Unit) {
    Box(
        modifier = Modifier
            .background(if (selected) Teal else Color(0x14FFFFFF), CircleShape)
            .border(1.dp, if (selected) Teal else Color(0x29CCE7FF), CircleShape)
            .clickable(onClick = onClick)
            .padding(horizontal = 15.dp, vertical = 10.dp)
    ) {
        Text(label, color = if (selected) Midnight else TextPrimary, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
fun BreathingOrb(size: Int = 160) {
    val transition = rememberInfiniteTransition(label = "breathing")
    val scale by transition.animateFloat(
        initialValue = 0.86f,
        targetValue = 1.08f,
        animationSpec = infiniteRepeatable(tween(4200), RepeatMode.Reverse),
        label = "orb-scale"
    )
    Box(
        Modifier.size(size.dp).scale(scale),
        contentAlignment = Alignment.Center
    ) {
        Box(Modifier.fillMaxSize().background(Cyan.copy(alpha = 0.18f), CircleShape))
        Box(Modifier.size((size * 0.62f).dp).background(Violet.copy(alpha = 0.44f), CircleShape))
    }
}

@Composable
fun ProgressRing(value: Int, label: String, modifier: Modifier = Modifier) {
    Box(modifier.size(94.dp), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val stroke = 8.dp.toPx()
            drawArc(Color.White.copy(alpha = 0.12f), -90f, 360f, false, style = Stroke(stroke, cap = StrokeCap.Round), size = Size(size.width, size.height))
            drawArc(Teal, -90f, 360f * value.coerceIn(0, 100) / 100f, false, style = Stroke(stroke, cap = StrokeCap.Round), size = Size(size.width, size.height))
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(value.toString(), color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 18.sp)
            Text(label, color = TextMuted, fontSize = 10.sp)
        }
    }
}

@Composable
fun SimpleLineChart(values: List<Int>, modifier: Modifier = Modifier) {
    Canvas(modifier.fillMaxWidth().height(120.dp)) {
        if (values.size < 2) return@Canvas
        val min = values.minOrNull() ?: 0
        val max = values.maxOrNull() ?: 100
        val range = (max - min).coerceAtLeast(1)
        var previous: Offset? = null
        values.forEachIndexed { index, value ->
            val x = size.width * index / (values.lastIndex)
            val y = size.height - ((value - min).toFloat() / range.toFloat()) * (size.height - 18.dp.toPx()) - 9.dp.toPx()
            val point = Offset(x, y)
            previous?.let { drawLine(Teal, it, point, strokeWidth = 4.dp.toPx(), cap = StrokeCap.Round) }
            drawCircle(Violet, 4.dp.toPx(), point)
            previous = point
        }
    }
}

@Composable
fun CenterText(text: String, modifier: Modifier = Modifier) {
    Text(text, color = TextMuted, fontSize = 15.sp, lineHeight = 22.sp, textAlign = TextAlign.Center, modifier = modifier)
}
