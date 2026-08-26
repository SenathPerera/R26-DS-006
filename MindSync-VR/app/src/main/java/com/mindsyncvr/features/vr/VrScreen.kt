package com.mindsyncvr.features.vr

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.MindSyncActions
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.core.model.VrStatus
import com.mindsyncvr.navigation.Routes

@Composable
fun VrScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    MindSyncScaffold {
        SectionHeader("VR connection", "Pair the Unity-based VR meditation experience with this mobile controller.")
        GlassCard {
            Text("Setup guide", color = TextPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
            Text("Open the MindSync Unity VR environment, confirm network/backend access, then enter the pairing code shown here.", color = TextMuted, lineHeight = 22.sp)
            PrimaryButton("Generate pairing code") { actions.pairVr() }
        }
        GlassCard {
            Text(state.vrDevice?.pairingCode ?: "MSVR-____", color = TextPrimary, fontSize = 34.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
            StatusPill(state.vrStatus.name, if (state.vrStatus == VrStatus.Ready) Green else Amber)
            Text("Transport: ${state.vrDevice?.transport ?: "backend bridge placeholder"}", color = TextMuted)
        }
        PrimaryButton("Continue to session handoff") {
            actions.createSession()
            navigate(Routes.Ready)
        }
    }
}
