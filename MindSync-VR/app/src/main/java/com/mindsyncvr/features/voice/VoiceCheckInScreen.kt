package com.mindsyncvr.features.voice

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.MindSyncActions
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.core.model.CompanionTurn
import com.mindsyncvr.core.model.VoiceCheckInState
import com.mindsyncvr.core.model.VoiceStage
import com.mindsyncvr.core.voice.CaptureParams
import com.mindsyncvr.core.voice.SessionPhase
import com.mindsyncvr.core.voice.VoiceRecorder
import com.mindsyncvr.core.voice.rememberTtsSpeaker
import com.mindsyncvr.navigation.Routes
import java.util.Locale

@Composable
fun VoiceCheckInScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    val vc = state.voiceCheckIn
    LaunchedEffect(Unit) { if (!vc.active) actions.startVoiceCheckIn() }

    val context = LocalContext.current
    val recorder = remember { VoiceRecorder() }
    val progress by recorder.progress.collectAsState()
    DisposableEffect(Unit) { onDispose { recorder.cancel() } }

    // Microphone permission — requested up front; automatic capture needs it.
    var micGranted by remember {
        mutableStateOf(context.checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED)
    }
    val permLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { micGranted = it }
    LaunchedEffect(Unit) { if (!micGranted) permLauncher.launch(Manifest.permission.RECORD_AUDIO) }

    // Companion speaks each new line aloud.
    val speaker = rememberTtsSpeaker()
    val turns = if (vc.stage == VoiceStage.PostConversation) vc.conversationPost else vc.conversationPre
    val lastCompanion = turns.lastOrNull { !it.fromUser }?.text
    LaunchedEffect(lastCompanion) { lastCompanion?.let { speaker.speak(it) } }

    // AUTOMATIC capture engine — no buttons. Re-arms whenever the flow asks
    // (captureToken bumps) as long as the mic is granted and we're not mid-clip.
    LaunchedEffect(vc.captureToken, vc.awaitingAmbient, vc.awaitingCapture, micGranted) {
        if (!micGranted || recorder.isActive) return@LaunchedEffect
        when {
            vc.awaitingAmbient -> recorder.start(0, CaptureParams.AMBIENT_SEC, 99.0) { payload, _ ->
                actions.submitAmbientClip(payload)
            }
            vc.awaitingCapture -> {
                val phase = if (vc.stage == VoiceStage.PostConversation) SessionPhase.Post else SessionPhase.Pre
                recorder.start(CaptureParams.MIN_SPEECH_SEC, CaptureParams.MAX_SEC, CaptureParams.SILENCE_TAIL_SEC) { payload, sec ->
                    actions.submitVoiceCapture(phase, payload, sec)
                }
            }
        }
    }

    MindSyncScaffold {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text(stageLabel(vc.stage), color = TextMuted, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
            when (vc.backendHealthy) {
                true -> StatusPill("Companion ready", Green)
                false -> StatusPill("Service offline", Amber)
                null -> StatusPill("Connecting…", Cyan)
            }
        }

        if (!micGranted) {
            GlassCard {
                Text("Microphone needed", color = Rose, fontWeight = FontWeight.Bold, fontSize = 16.sp)
                Text("The companion listens to your voice — please allow the microphone to continue.", color = TextMuted, fontSize = 14.sp)
                PrimaryButton("Allow microphone") { permLauncher.launch(Manifest.permission.RECORD_AUDIO) }
            }
        }

        if (vc.error != null) {
            GlassCard {
                Text("Let's try again", color = Rose, fontWeight = FontWeight.Bold, fontSize = 16.sp)
                Text(vc.error, color = TextMuted, fontSize = 14.sp, lineHeight = 20.sp)
            }
        }

        when (vc.stage) {
            VoiceStage.Environment -> EnvironmentStage(vc, progress)
            VoiceStage.PreConversation -> ConversationStage(
                phase = SessionPhase.Pre, vc = vc, progress = progress,
                onContinue = { actions.advanceVoiceStage(VoiceStage.VrSession) },
            )
            VoiceStage.VrSession -> VrHandoffStage(onBack = { actions.advanceVoiceStage(VoiceStage.PostConversation) })
            VoiceStage.PostConversation -> ConversationStage(
                phase = SessionPhase.Post, vc = vc, progress = progress,
                onContinue = { actions.completeVoiceCheckIn() },
            )
            VoiceStage.Report -> ReportStage(vc) { actions.endVoiceCheckIn(); navigate(Routes.Home) }
        }

        if (vc.stage != VoiceStage.Report) {
            SecondaryButton("Exit", danger = true) { actions.endVoiceCheckIn(); navigate(Routes.Home) }
        }
    }
}

// --------------------------------------------------------------------- stages

@Composable
private fun EnvironmentStage(vc: VoiceCheckInState, progress: com.mindsyncvr.core.voice.CaptureProgress) {
    SectionHeader("Let's check the room", "Layer 1 — a quiet space keeps your reading accurate.")
    GlassCard { ConversationView(vc.conversationPre, thinking = false) }
    GlassCard {
        Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            when {
                vc.checkingAmbient -> { BreathingOrb(110); Text("Checking the room…", color = TextPrimary, fontWeight = FontWeight.Bold) }
                progress.active -> { ListeningRing(progress.amplitude, "Listening to the room"); Text("Please stay silent…", color = TextMuted, fontSize = 13.sp) }
                else -> { BreathingOrb(110); CenterText("Getting ready to listen…") }
            }
        }
    }
}

@Composable
private fun ConversationStage(phase: SessionPhase, vc: VoiceCheckInState, progress: com.mindsyncvr.core.voice.CaptureProgress, onContinue: () -> Unit) {
    val analysis = if (phase == SessionPhase.Pre) vc.pre else vc.post
    val turns = if (phase == SessionPhase.Pre) vc.conversationPre else vc.conversationPost

    SectionHeader(
        if (phase == SessionPhase.Pre) "Talk with your companion" else "How are you now?",
        "Layer 2 — just speak naturally. I'm listening in the background; there's nothing to press.",
    )
    GlassCard { ConversationView(turns, vc.companionThinking) }

    when {
        vc.analyzing -> GlassCard {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
                BreathingOrb(110); Text("Reading your voice…", color = TextPrimary, fontWeight = FontWeight.Bold)
                CenterText("This first read can take a moment while the model warms up.")
            }
        }
        analysis != null -> {
            GlassCard { AnalysisSummary(analysis) }
            PrimaryButton(if (phase == SessionPhase.Pre) "Start your session" else "See your report") { onContinue() }
        }
        progress.active -> GlassCard {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
                ListeningRing(progress.amplitude, if (progress.speaking) "Listening…" else "I'm here")
                Text("${progress.speechSec}s of your voice", color = TextMuted, fontSize = 13.sp)
                if (progress.speechSec < CaptureParams.MIN_SPEECH_SEC) CenterText("Keep going — tell me a little more.")
            }
        }
        else -> GlassCard {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) { BreathingOrb(100); CenterText("Getting ready to listen…") }
        }
    }
}

@Composable
private fun VrHandoffStage(onBack: () -> Unit) {
    SectionHeader("Your calm session", "Take your 20–30 minute VR session now. Come back when you're done.")
    GlassCard {
        Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(14.dp)) {
            BreathingOrb(180)
            CenterText("Breathe naturally. When your session finishes, we'll check in once more.")
        }
    }
    PrimaryButton("I'm back — check in again") { onBack() }
}

// --------------------------------------------------------------------- pieces

@Composable
private fun ConversationView(turns: List<CompanionTurn>, thinking: Boolean) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        turns.forEach { turn ->
            val tone = if (turn.fromUser) Cyan else Teal
            Column(Modifier.fillMaxWidth(), horizontalAlignment = if (turn.fromUser) Alignment.End else Alignment.Start) {
                Text(if (turn.fromUser) "You" else "Companion", color = tone, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                Text(turn.text, color = TextPrimary, fontSize = 16.sp, lineHeight = 23.sp)
            }
        }
        if (thinking) Text("Companion is listening…", color = TextMuted, fontSize = 13.sp)
    }
}

@Composable
private fun ListeningRing(amplitude: Float, label: String) {
    val size = (120 + amplitude * 40).dp
    Box(Modifier.size(160.dp), contentAlignment = Alignment.Center) {
        Box(Modifier.size(size).background(Teal.copy(alpha = 0.18f + amplitude * 0.25f), CircleShape), contentAlignment = Alignment.Center) {
            Text("🎧", fontSize = 40.sp)
        }
    }
    Text(label, color = Teal, fontWeight = FontWeight.Bold, fontSize = 15.sp)
}

@Composable
private fun AnalysisSummary(a: com.mindsyncvr.core.voice.VoiceAnalysis) {
    Text("Voice stress reading", color = Teal, fontSize = 12.sp, fontWeight = FontWeight.Bold)
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.Bottom) {
        Text("${fmt(a.stressScore)} / 10", color = TextPrimary, fontSize = 30.sp, fontWeight = FontWeight.Bold)
        StatusPill(a.stressLevel.replaceFirstChar { it.uppercase() }, levelTone(a.stressLevel))
    }
    Text("A single reading is only a signal — the change from before to after is what matters most.", color = TextMuted, fontSize = 12.sp, lineHeight = 18.sp)
    if (a.inputLevel == "faint") Text("That was a little quiet — speaking up a touch reads more reliably.", color = Amber, fontSize = 12.sp)
}

@Composable
private fun ReportStage(vc: VoiceCheckInState, onDone: () -> Unit) {
    SectionHeader("Your session report", "The primary signal is how your stress changed, not a single number.")

    if (vc.generatingReport || vc.report == null) {
        GlassCard {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                BreathingOrb(120); Text("Bringing it together…", color = TextPrimary, fontWeight = FontWeight.Bold)
            }
        }
        return
    }

    val r = vc.report
    val c = r.comparison
    val (headline, tone) = when (c.direction) {
        "improved" -> "Your stress eased" to Green
        "worsened" -> "Your stress rose" to Rose
        else -> "No clear change this time" to Cyan
    }

    GlassCard {
        Text("Layers 2 + 3 — voice & change", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        Text(headline, color = tone, fontSize = 24.sp, fontWeight = FontWeight.Bold)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(24.dp)) {
            ScoreBlock("Before", c.preStress)
            ScoreBlock("After", c.postStress)
            ScoreBlock("Change", c.delta, signed = true)
        }
        Text(r.verdict.note, color = TextMuted, fontSize = 12.sp, lineHeight = 18.sp)
    }

    GlassCard {
        Text("Layer 4 — voice × heart cross-check", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        if (r.crossmodal == null) {
            Text("Not available for this session — the voice reading stands on its own.", color = TextMuted, fontSize = 14.sp)
        } else {
            val cm = r.crossmodal
            Text(
                if (cm.validated) "Voice and heart signals agree." else cm.mismatchType?.replace('_', ' ')?.replaceFirstChar { it.uppercase() } ?: "Signals were inconclusive.",
                color = TextPrimary, fontSize = 15.sp,
            )
            Text("Agreement ${fmt(cm.agreement * 10)}/10", color = TextMuted, fontSize = 12.sp)
        }
    }

    GlassCard {
        Text("Layer 5 — session pattern", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        val an = r.anomaly
        Text(
            when {
                an == null -> "Not available for this session."
                !an.anomaly -> "This session looked typical — nothing unusual."
                an.anomalyDirection == "unusual_improvement" -> "This session shifted more than your usual — in a good way."
                else -> "This session looked a little different from your usual pattern."
            },
            color = TextPrimary, fontSize = 15.sp, lineHeight = 21.sp,
        )
    }

    GlassCard {
        Text("Compared to your own normal", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        Text(
            r.personalBaseline.relativeBand?.replaceFirstChar { it.uppercase() } ?: r.personalBaseline.note ?: "Building your personal baseline.",
            color = TextPrimary, fontSize = 15.sp,
        )
    }

    Text("Research prototype — a wellbeing signal, not a medical diagnosis.", color = TextMuted, fontSize = 11.sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
    PrimaryButton("Done") { onDone() }
}

@Composable
private fun ScoreBlock(label: String, value: Double, signed: Boolean = false) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(if (signed && value > 0) "+${fmt(value)}" else fmt(value), color = TextPrimary, fontSize = 22.sp, fontWeight = FontWeight.Bold)
        Text(label, color = TextMuted, fontSize = 11.sp)
    }
}

private fun stageLabel(stage: VoiceStage) = when (stage) {
    VoiceStage.Environment -> "Step 1 · Environment"
    VoiceStage.PreConversation -> "Step 2 · Before session"
    VoiceStage.VrSession -> "Step 3 · Your session"
    VoiceStage.PostConversation -> "Step 4 · After session"
    VoiceStage.Report -> "Step 5 · Report"
}

private fun levelTone(level: String): Color = when (level.lowercase()) {
    "no" -> Green
    "mild" -> Cyan
    "moderate" -> Amber
    else -> Rose
}

private fun fmt(d: Double) = String.format(Locale.US, "%.1f", d)
