package com.mindsyncvr.features.analytics

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.model.AppState

@Composable
fun AnalyticsScreen(state: AppState) {
    MindSyncScaffold {
        SectionHeader("Trends", "Session history, stress trends, mood shifts, and validation status.")
        GlassCard {
            Text("Stress trend", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            SimpleLineChart(listOf(48, 42, 39, 34, 31, 28, 24))
        }
        state.sessions.forEach { session ->
            GlassCard {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Column(Modifier.weight(1f)) {
                        Text(session.title, color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                        Text("${session.durationMinutes} min · ${session.environment} · ${session.audioProfile}", color = TextMuted)
                    }
                    StatusPill(if (session.validationComplete) "Validated" else "Pending", if (session.validationComplete) Green else Amber)
                }
                Text("Mood ${session.moodBefore} to ${session.moodAfter} · Completion ${session.completionRate}%", color = TextMuted)
                if (session.notes.isNotBlank()) Text(session.notes, color = TextMuted)
            }
        }
    }
}
