package com.mindsyncvr.core.design

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val Midnight = Color(0xFF07111F)
val Elevated = Color(0xFF0C1A2E)
val SurfaceGlass = Color(0x1FFFFFFF)
val SurfaceStrong = Color(0x2EFFFFFF)
val TextPrimary = Color(0xFFF4F1EA)
val TextMuted = Color(0xFFAEB8CE)
val Teal = Color(0xFF69E0D3)
val Cyan = Color(0xFF76D9FF)
val Violet = Color(0xFFBCA7FF)
val Indigo = Color(0xFF6D79FF)
val Rose = Color(0xFFFF9BB2)
val Amber = Color(0xFFF4D27A)
val Green = Color(0xFF7FE2A0)
val Danger = Color(0xFFFF7A8A)

private val MindSyncColors = darkColorScheme(
    primary = Teal,
    secondary = Violet,
    tertiary = Cyan,
    background = Midnight,
    surface = Elevated,
    onPrimary = Midnight,
    onSecondary = Midnight,
    onBackground = TextPrimary,
    onSurface = TextPrimary,
    error = Danger
)

@Composable
fun MindSyncTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = MindSyncColors,
        typography = MaterialTheme.typography,
        content = content
    )
}
