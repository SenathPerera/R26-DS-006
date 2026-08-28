package com.mindsyncvr.features.dashboard

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.core.model.ConnectionState
import com.mindsyncvr.core.model.VrStatus
import com.mindsyncvr.navigation.Routes

@Composable
fun HomeScreen(state: AppState, navigate: (String) -> Unit) {
    val telemetry = state.bleIngestion.latestTelemetry
    val isStreaming = state.bleIngestion.isStreaming && telemetry != null
    val dataReadiness = if (isStreaming) 100 else 0
    val pulseSummary = if (isStreaming) {
        "Live wearable telemetry is streaming. IR ${telemetry?.ir ?: "-"}, RED ${telemetry?.red ?: "-"}, noise peak ${telemetry?.noisePeak ?: "-"}."
    } else {
        "Connect the wearable to stream MAX30100 and INMP441 readings."
    }

    MindSyncScaffold {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Column {
                Text("Good to see you", color = TextMuted, fontSize = 13.sp)
                Text(state.user?.name ?: "Participant", color = TextPrimary, fontSize = 30.sp, fontWeight = FontWeight.Bold)
            }
            StatusPill(if (state.pendingValidationCount > 0) "${state.pendingValidationCount} validation pending" else "Study flow complete", if (state.pendingValidationCount > 0) Amber else Green)
        }

        GlassCard {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Current inner state", color = TextPrimary, fontSize = 22.sp, fontWeight = FontWeight.Bold)
                    Text(pulseSummary, color = TextMuted, lineHeight = 22.sp)
                }
                ProgressRing(dataReadiness, "live")
            }
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                BreathingOrb(104)
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    StatusPill(if (state.wearableState == ConnectionState.Connected) "Wearable streaming" else "Wearable not connected", if (state.wearableState == ConnectionState.Connected) Green else Amber)
                    StatusPill("IR ${telemetry?.ir ?: "--"}", if (telemetry?.ir != null) Green else Amber)
                    StatusPill("RED ${telemetry?.red ?: "--"}", if (telemetry?.red != null) Green else Amber)
                    StatusPill("Noise ${telemetry?.noiseAverage ?: "--"}", if (telemetry?.noiseAverage != null) Green else Amber)
                    StatusPill(if (state.vrStatus == VrStatus.Ready) "VR ready" else "VR setup needed", if (state.vrStatus == VrStatus.Ready) Green else Amber)
                }
            }
            // Single launch hook for Component D's voice flow (Prathikesh). The
            // voice check-in owns Layer 1 -> 5 and the VR hand-off internally.
            PrimaryButton("Begin Session") { navigate(Routes.VoiceCheckIn) }
        }

        GlassCard {
            Text("Recommended focus", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            Text("15-minute Ocean Dusk session with warm pads, low-motion visuals, and post-session Component D validation.", color = TextMuted, lineHeight = 22.sp)
        }

        SectionHeader("Control hub", "Orchestrate wearable, backend, VR, session, validation, and analytics.")
        listOf(
            "Connect wearable" to Routes.Wearable,
            "Connect VR" to Routes.Vr,
            "Start session" to Routes.PreSession,
            "Questionnaires" to Routes.Questionnaires,
            "Session history" to Routes.Analytics,
            "Settings" to Routes.Settings
        ).chunked(2).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                row.forEach { (label, route) ->
                    GlassCard(Modifier.weight(1f)) {
                        Text(label, color = TextPrimary, fontWeight = FontWeight.Bold)
                        SecondaryButton("Open") { navigate(route) }
                    }
                }
            }
        }
    }
}
