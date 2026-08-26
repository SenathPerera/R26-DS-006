@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.mindsyncvr.features.session

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.MindSyncActions
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.core.model.StressBand
import com.mindsyncvr.features.questionnaire.QuestionnaireRenderer
import com.mindsyncvr.navigation.Routes

@Composable
fun PreSessionScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    val template = state.questionnaireTemplates.first { it.component == "pre_session" }
    MindSyncScaffold {
        SectionHeader("Pre-session check-in", "A brief baseline helps the adaptive system start gently.")
        QuestionnaireRenderer(template = template, submitLabel = "Continue to readiness") {
            navigate(Routes.Ready)
        }
    }
}

@Composable
fun ReadyScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    MindSyncScaffold {
        GlassCard {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                BreathingOrb(150)
                Text("Ready to begin", color = TextPrimary, fontSize = 28.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
                CenterText("Start only when seated comfortably. You can pause, stop, or report discomfort at any time.")
                StatusPill("Wearable ${state.wearableState.name}", if (state.selectedWearable != null) Green else Amber)
                StatusPill("VR ${state.vrStatus.name}", if (state.vrDevice != null) Green else Amber)
            }
        }
        PrimaryButton("Start live monitor") {
            val sessionId = state.activeSession?.id ?: actions.createSession()
            actions.startLiveSession(sessionId)
            navigate(Routes.Live)
        }
        SecondaryButton("Grounding exit") { navigate(Routes.Home) }
    }
}

@Composable
fun LiveSessionScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    val sessionId = state.activeSession?.id ?: "session-live-demo"
    LaunchedEffect(sessionId) {
        if (state.liveSession == null) actions.startLiveSession(sessionId)
    }
    val live = state.liveSession
    val research = live?.research
    MindSyncScaffold {
        Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
            Text("Live adaptive session", color = TextMuted)
            Text(formatTime(live?.elapsedSeconds ?: 0), color = TextPrimary, fontSize = 34.sp, fontWeight = FontWeight.Bold)
        }
        Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            BreathingOrb(210)
            Text("Breathe naturally", color = TextPrimary, fontSize = 24.sp, fontWeight = FontWeight.Bold)
            CenterText(research?.stressSummary ?: "Preparing live biofeedback stream")
        }
        GlassCard {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                ProgressRing(research?.stressLevel ?: 0, "stress")
                ProgressRing(research?.signalConfidence ?: 0, "signal")
                ProgressRing(research?.soundAdaptationLevel ?: 0, "audio")
            }
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                StatusPill("Stress ${research?.stressBand ?: StressBand.Balanced}", if (research?.stressBand == StressBand.High) Danger else Green)
                StatusPill(research?.vrAdaptationState ?: "VR waiting")
                StatusPill(research?.therapeuticAudioMode ?: "Audio ready")
                StatusPill(if (live?.wearableConnected == true) "Wearable connected" else "Wearable waiting", if (live?.wearableConnected == true) Green else Amber)
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            SecondaryButton("Pause", Modifier.weight(1f)) { }
            SecondaryButton("Stop safely", Modifier.weight(1f), danger = true) { navigate(Routes.Complete) }
        }
    }
}

@Composable
fun SessionCompleteScreen(navigate: (String) -> Unit) {
    MindSyncScaffold {
        GlassCard {
            Text("Session complete", color = TextPrimary, fontSize = 28.sp, fontWeight = FontWeight.Bold)
            Text("Before returning to the dashboard, please complete the linked Component D validation so the research record is complete.", color = TextMuted, lineHeight = 22.sp)
        }
        PrimaryButton("Start post-session validation") { navigate(Routes.Questionnaires) }
        SecondaryButton("Return home") { navigate(Routes.Home) }
    }
}

private fun formatTime(seconds: Int): String {
    val minutes = seconds / 60
    val remain = seconds % 60
    return "%02d:%02d".format(minutes, remain)
}
