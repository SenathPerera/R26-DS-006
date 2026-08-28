package com.mindsyncvr.features.voice

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
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
import com.mindsyncvr.BuildConfig
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
import com.mindsyncvr.features.voice.components.AmbientQualityPanel
import com.mindsyncvr.features.voice.components.AvatarState
import com.mindsyncvr.features.voice.components.CircumplexPlot
import com.mindsyncvr.features.voice.components.CompanionAvatar
import com.mindsyncvr.features.voice.components.StressResultCard
import com.mindsyncvr.navigation.Routes
import kotlinx.coroutines.delay
import java.util.Locale

// Pause between the companion finishing a line and the mic opening, so the
// speaker tail and room echo decay before Layer 1 / Layer 2 capture (BUG-1).
private const val SETTLE_MS = 350L

@Composable
fun VoiceCheckInScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    val vc = state.voiceCheckIn
    LaunchedEffect(Unit) { if (!vc.active) actions.startVoiceCheckIn() }

    val context = LocalContext.current
    // Debug builds dump every captured WAV so a bad clip can be pulled + re-scored
    // via curl, settling "capture vs transport" (WP9). Never in release.
    val recorder = remember {
        VoiceRecorder(debugDir = if (BuildConfig.DEBUG) java.io.File(context.filesDir, "voice_debug") else null)
    }
    val progress by recorder.progress.collectAsState()
    DisposableEffect(Unit) { onDispose { recorder.cancel() } }

    // Microphone permission — requested up front; automatic capture needs it.
    var micGranted by remember {
        mutableStateOf(context.checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED)
    }
    val permLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { micGranted = it }
    LaunchedEffect(Unit) { if (!micGranted) permLauncher.launch(Manifest.permission.RECORD_AUDIO) }

    // STRICT TURN-TAKING (BUG-1): one sequential effect. The companion speaks the
    // newest line to completion with the mic CLOSED, the room is allowed to settle,
    // and only THEN does capture open — so the recorder can never pick up the
    // companion's own TTS. Re-runs on a new line or a re-arm (captureToken bump).
    val speaker = rememberTtsSpeaker(BuildConfig.COMPONENT_D_BASE_URL)
    val turns = if (vc.stage == VoiceStage.PostConversation) vc.conversationPost else vc.conversationPre
    val lastCompanion = turns.lastOrNull { !it.fromUser }?.text

    var companionSpeaking by remember { mutableStateOf(false) }
    LaunchedEffect(lastCompanion, vc.captureToken, micGranted) {
        // 1) speak the newest companion line to completion (microphone stays closed)
        lastCompanion?.let {
            companionSpeaking = true
            try { speaker.speakAndWait(it, vc.language) } finally { companionSpeaking = false }
        }
        // 2) let the speaker tail and room echo decay before listening
        delay(SETTLE_MS)
        // 3) only now open the mic, and only if a capture is actually armed
        if (!micGranted || recorder.isActive) return@LaunchedEffect
        when {
            vc.awaitingAmbient -> recorder.start(0, CaptureParams.AMBIENT_SEC, 99.0) { payload, _, _ ->
                actions.submitAmbientClip(payload)
            }
            vc.awaitingCapture -> {
                val phase = if (vc.stage == VoiceStage.PostConversation) SessionPhase.Post else SessionPhase.Pre
                // A turn ends after a little speech + a pause, OR if the person stays
                // silent — so short/quiet speakers get a follow-up and their PCM
                // accumulates across turns toward the cumulative budget (WP4).
                recorder.start(
                    minSpeechSec = CaptureParams.TURN_END_SPEECH_SEC,
                    maxSec = CaptureParams.MAX_SEC,
                    silenceTailSec = CaptureParams.SILENCE_TAIL_SEC,
                    noSpeechTimeoutSec = CaptureParams.NO_SPEECH_TIMEOUT_SEC,
                    minListenSec = CaptureParams.MIN_LISTEN_SEC,       // WP2: no cutoff before 12s
                    speechThreshold = vc.speechThresholdRms,           // WP2: adaptive from room floor
                ) { payload, pcm, sec ->
                    actions.submitVoiceCapture(phase, payload, pcm, sec)
                }
            }
        }
    }

    // WP7 — system back is mapped to stage semantics so it never ejects the person
    // out of Component D mid-flow. During analysis it's ignored (an in-flight /infer
    // must not be cancelled); while capturing or on an input stage it asks first;
    // on the VR hand-off / report / crisis it leaves cleanly.
    var confirmExit by remember { mutableStateOf(false) }
    var ignoredBackDuringAnalysis by remember { mutableStateOf(false) }
    fun leave() { recorder.cancel(); actions.endVoiceCheckIn(); navigate(Routes.Home) }
    BackHandler(enabled = !vc.crisis) {
        when {
            vc.analyzing -> ignoredBackDuringAnalysis = true            // never cancel a running read
            vc.stage == VoiceStage.VrSession || vc.stage == VoiceStage.Report -> leave()
            else -> confirmExit = true                                  // Intro / Environment / Pre / Post
        }
    }
    if (confirmExit) {
        androidx.compose.material3.AlertDialog(
            onDismissRequest = { confirmExit = false },
            confirmButton = { androidx.compose.material3.TextButton(onClick = { confirmExit = false; leave() }) { Text("Leave", color = Rose) } },
            dismissButton = { androidx.compose.material3.TextButton(onClick = { confirmExit = false }) { Text("Stay", color = Teal) } },
            title = { Text("Leave the check-in?", color = TextPrimary) },
            text = { Text("Your progress in this check-in won't be saved.", color = TextMuted) },
            containerColor = Elevated,
        )
    }
    if (ignoredBackDuringAnalysis) {
        LaunchedEffect(Unit) { delay(1800); ignoredBackDuringAnalysis = false }
    }

    MindSyncScaffold {
        if (ignoredBackDuringAnalysis) {
            GlassCard { Text("Just a moment — I'm reading your voice.", color = TextPrimary, fontSize = 14.sp) }
        }
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

        if (vc.crisis) {
            CrisisCard(vc.crisisReply)
        } else when (vc.stage) {
            VoiceStage.Intro -> IntroStage(vc) { name, lang -> actions.beginEnvironmentCheck(name, lang) }
            VoiceStage.Environment -> EnvironmentStage(vc, progress, companionSpeaking) { actions.continueFromEnvironment() }
            VoiceStage.PreConversation -> ConversationStage(
                phase = SessionPhase.Pre, vc = vc, progress = progress, speaking = companionSpeaking,
                onContinue = { actions.advanceVoiceStage(VoiceStage.VrSession) },
            )
            VoiceStage.VrSession -> VrHandoffStage(onBack = { actions.advanceVoiceStage(VoiceStage.PostConversation) })
            VoiceStage.PostConversation -> ConversationStage(
                phase = SessionPhase.Post, vc = vc, progress = progress, speaking = companionSpeaking,
                onContinue = { actions.completeVoiceCheckIn() },
            )
            VoiceStage.Report -> ReportStage(vc) { actions.endVoiceCheckIn(); navigate(Routes.Home) }
        }

        // Debug-only: force simulated HRV so all five layers can be demoed to a
        // supervisor without Component B connected. Never present in release builds.
        if (BuildConfig.DEBUG && vc.stage != VoiceStage.Report && !vc.crisis) {
            SecondaryButton(if (vc.debugForceMockHrv) "Demo: mock wristband ON" else "Demo: mock wristband OFF") {
                actions.setDebugMockHrv(!vc.debugForceMockHrv)
            }
        }

        if (vc.stage != VoiceStage.Report) {
            SecondaryButton("Exit", danger = true) { actions.endVoiceCheckIn(); navigate(Routes.Home) }
        }
    }
}

// --------------------------------------------------------------------- stages

@Composable
private fun IntroStage(vc: VoiceCheckInState, onContinue: (String, String) -> Unit) {
    var name by remember { mutableStateOf(vc.personName.takeIf { it.isNotBlank() && it != "there" } ?: "") }
    var language by remember { mutableStateOf(vc.language) }

    SectionHeader("Before we begin", "So your companion knows what to call you.")
    GlassCard {
        Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text("What should I call you?", color = TextPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
            OutlinedTextField(
                value = name, onValueChange = { name = it },
                singleLine = true,
                placeholder = { Text("Your first name", color = TextMuted) },
                modifier = Modifier.fillMaxWidth(),
            )
            Text("Which language will you speak?", color = TextPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                LanguageChip("English", language == "english") { language = "english" }
                LanguageChip("සිංහල", language == "sinhala") { language = "sinhala" }
            }
            if (language == "sinhala") {
                Text("Sinhala voice understanding is still improving — English reads more reliably for now.", color = Amber, fontSize = 12.sp, lineHeight = 17.sp)
            }
        }
    }
    PrimaryButton("Continue") { onContinue(name, language) }
}

@Composable
private fun LanguageChip(label: String, selected: Boolean, onClick: () -> Unit) {
    val bg = if (selected) Teal.copy(alpha = 0.28f) else SurfaceGlass
    Box(
        Modifier.background(bg, CircleShape).padding(horizontal = 20.dp, vertical = 10.dp).clickable { onClick() },
        contentAlignment = Alignment.Center,
    ) {
        Text(label, color = if (selected) Teal else TextMuted, fontWeight = FontWeight.Bold, fontSize = 15.sp)
    }
}

@Composable
private fun EnvironmentStage(vc: VoiceCheckInState, progress: com.mindsyncvr.core.voice.CaptureProgress, speaking: Boolean, onContinue: () -> Unit) {
    SectionHeader("Let's check the room", "Layer 1 — a quiet space keeps your reading accurate.")
    vc.conversationPre.lastOrNull { !it.fromUser }?.text?.let {
        GlassCard { CompanionUtterance(it) }
    }
    vc.ambient?.let { AmbientQualityPanel(it, attempts = vc.ambientAttempts) }
    if (vc.ambientOk == true) {
        // Passed — the person has seen their score; let them tap through.
        PrimaryButton("Continue to the check-in") { onContinue() }
    } else {
        GlassCard {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                when {
                    vc.checkingAmbient -> { CompanionAvatar(AvatarState.Thinking, size = 130); Text("Checking the room…", color = TextPrimary, fontWeight = FontWeight.Bold) }
                    progress.active -> {
                        val remaining = (CaptureParams.AMBIENT_SEC - progress.elapsedSec).coerceAtLeast(0)
                        ListeningRing(progress.amplitude, "Listening to the room")
                        Text("Please stay silent… ${remaining}s", color = TextMuted, fontSize = 13.sp)
                    }
                    speaking -> { CompanionAvatar(AvatarState.Speaking, size = 130); CenterText("…") }
                    else -> { CompanionAvatar(AvatarState.Idle, size = 130); CenterText("Getting ready to listen…") }
                }
            }
        }
    }
}

@Composable
private fun ConversationStage(phase: SessionPhase, vc: VoiceCheckInState, progress: com.mindsyncvr.core.voice.CaptureProgress, speaking: Boolean, onContinue: () -> Unit) {
    val analysis = if (phase == SessionPhase.Pre) vc.pre else vc.post
    val turns = if (phase == SessionPhase.Pre) vc.conversationPre else vc.conversationPost

    val lastCompanion = turns.lastOrNull { !it.fromUser }?.text
    val lastUser = turns.lastOrNull { it.fromUser }?.text

    SectionHeader(
        if (phase == SessionPhase.Pre) "Talk with your companion" else "How are you now?",
        "Layer 2 — just speak naturally. I'm listening in the background; there's nothing to press.",
    )

    if (analysis != null) {
        // WP4 — the full reading (valence, arousal, confidence, type, quality,
        // "why this reading"), not just a number, at the end of BOTH phases.
        StressResultCard(analysis)
        if (analysis.inputLevel == "faint") GlassCard {
            Text("That was a little quiet — speaking up a touch reads more reliably.", color = Amber, fontSize = 12.sp)
        }
        analysis.body?.let { b ->
            GlassCard { Text("Wristband right now: ${b.level.replaceFirstChar { it.uppercase() }} · live", color = Green, fontSize = 12.sp, fontWeight = FontWeight.SemiBold) }
        }
        Text("A single reading is only a signal — the change from before to after is what matters most.",
            color = TextMuted, fontSize = 12.sp, lineHeight = 18.sp)
        ConversationHistory(turns)
        PrimaryButton(if (phase == SessionPhase.Pre) "Start your session" else "See your report") { onContinue() }
        return
    }

    // WP3 — ONE thing at a time. The avatar, the single current companion line
    // (cross-fading), and a brief echo of what was heard — no growing chat log,
    // and a reserved height so nothing above the line jumps as it changes.
    val st = when {
        vc.analyzing || vc.companionThinking -> AvatarState.Thinking
        speaking -> AvatarState.Speaking
        progress.active -> AvatarState.Listening
        else -> AvatarState.Idle
    }
    val status = when {
        vc.analyzing -> "Reading your voice…"
        vc.companionThinking -> "Thinking about what you said…"
        speaking -> "…"
        progress.active -> if (progress.speaking) "I'm listening…" else "Take your time"
        else -> "Getting ready to listen…"
    }
    GlassCard {
        Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            CompanionAvatar(st, amplitude = progress.amplitude, size = 150)
            CompanionUtterance(lastCompanion)
            Text(status, color = Teal, fontWeight = FontWeight.Bold, fontSize = 15.sp)
            when {
                progress.active -> Text("${progress.speechSec}s of your voice", color = TextMuted, fontSize = 13.sp)
                vc.analyzing -> CenterText("This first read can take a moment while the model warms up.")
                st == AvatarState.Idle -> CenterText("Speak as long as you like — I'll wait until you're done.")
            }
            UserEcho(lastUser)
        }
    }
    ConversationHistory(turns)
}

@Composable
private fun CrisisCard(reply: String?) {
    // Distinct, terminal state: the companion's calm reply, then real support
    // information. Scoring has already stopped in the repository — this is never
    // a diagnosis, only a caring hand-off to people who can help.
    SectionHeader("I'm here with you", "Let's pause the check-in for a moment.")
    GlassCard {
        Text(reply ?: "It sounds like you're carrying something really heavy right now, and I'm glad you said it out loud.",
            color = TextPrimary, fontSize = 16.sp, lineHeight = 23.sp)
    }
    GlassCard {
        Text("Someone to talk to, any time", color = Teal, fontSize = 12.sp, fontWeight = FontWeight.Bold)
        Text("If things feel like too much, please reach out — you don't have to hold it alone:",
            color = TextMuted, fontSize = 14.sp, lineHeight = 20.sp)
        Spacer(Modifier.height(8.dp))
        Text("• National Mental Health Helpline — 1926", color = TextPrimary, fontSize = 15.sp)
        Text("• Sri Lanka Sumithrayo — 011 269 6666", color = TextPrimary, fontSize = 15.sp)
        Text("• CCCline (emotional support) — 1333", color = TextPrimary, fontSize = 15.sp)
        Text("If you're in immediate danger, please call 1990 (Suwa Seriya) or go to the nearest hospital.",
            color = TextMuted, fontSize = 12.sp, lineHeight = 18.sp, modifier = Modifier.padding(top = 8.dp))
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

/** WP3 — the single current companion line, cross-fading between utterances.
 *  A reserved min-height keeps the avatar above it from jumping as lines change. */
@Composable
private fun CompanionUtterance(text: String?) {
    Box(Modifier.fillMaxWidth().heightIn(min = 96.dp), contentAlignment = Alignment.Center) {
        AnimatedContent(
            targetState = text ?: "",
            transitionSpec = {
                (fadeIn(tween(300)) + slideInVertically(tween(300)) { it / 6 })
                    .togetherWith(fadeOut(tween(220)))
            },
            label = "utterance",
        ) { line ->
            Text(line, color = TextPrimary, fontSize = 18.sp, lineHeight = 26.sp,
                textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
        }
    }
}

/** WP3 — a brief, lighter echo of what the person just said, then it fades. */
@Composable
private fun UserEcho(text: String?) {
    var visible by remember(text) { mutableStateOf(text != null) }
    LaunchedEffect(text) { if (text != null) { visible = true; delay(4500); visible = false } }
    AnimatedVisibility(visible = visible && text != null, enter = fadeIn(tween(300)), exit = fadeOut(tween(600))) {
        Text("“${text.orEmpty()}”", color = TextMuted, fontSize = 13.sp, lineHeight = 19.sp,
            textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
    }
}

/** WP3 — the full turn history, collapsed by default so it never becomes a
 *  scrolling chat log during the conversation. */
@Composable
private fun ConversationHistory(turns: List<CompanionTurn>) {
    if (turns.isEmpty()) return
    var open by remember { mutableStateOf(false) }
    GlassCard {
        Row(Modifier.fillMaxWidth().clickable { open = !open }, horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text("Conversation so far (${turns.size})", color = TextMuted, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
            Text(if (open) "▲" else "▼", color = TextMuted, fontSize = 12.sp)
        }
        if (open) {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                turns.forEach { turn ->
                    val tone = if (turn.fromUser) Cyan else Teal
                    Column(Modifier.fillMaxWidth(), horizontalAlignment = if (turn.fromUser) Alignment.End else Alignment.Start) {
                        Text(if (turn.fromUser) "You" else "Companion", color = tone, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                        Text(turn.text, color = TextPrimary, fontSize = 15.sp, lineHeight = 22.sp)
                    }
                }
            }
        }
    }
}

@Composable
private fun ListeningRing(amplitude: Float, label: String) {
    // The companion's live presence — the geometric avatar (WP8) replaces the old
    // 🎧 emoji, reacting to the mic level while listening.
    CompanionAvatar(state = AvatarState.Listening, amplitude = amplitude, size = 150)
    Text(label, color = Teal, fontWeight = FontWeight.Bold, fontSize = 15.sp)
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
    val improved = c.direction == "improved"
    val worsened = c.direction == "worsened"
    val (headline, tone) = when (c.direction) {
        "improved" -> "The session helped — your stress eased" to Green
        "worsened" -> "Your stress read higher after" to Rose
        else -> "Your stress stayed about the same" to Cyan
    }

    // 1) Headline + BigMetrics (Before / After / Change / Outcome).
    GlassCard {
        Text(headline, color = tone, fontSize = 24.sp, fontWeight = FontWeight.Bold, lineHeight = 30.sp)
        Text(sessionSummaryLine(c.direction, c.magnitude), color = TextPrimary, fontSize = 14.sp, lineHeight = 21.sp)
        Spacer(Modifier.height(4.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            BigMetric("Before", "${fmt(c.preStress)}/10", levelWord(vc.pre?.stressLevel, c.preStress), Amber, Modifier.weight(1f))
            BigMetric("After", "${fmt(c.postStress)}/10", levelWord(vc.post?.stressLevel, c.postStress), if (improved) Green else TextMuted, Modifier.weight(1f))
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            val arrow = if (c.delta < 0) "↓" else if (c.delta > 0) "↑" else "→"
            BigMetric("Change", "$arrow ${fmt(kotlin.math.abs(c.delta))}", if (c.magnitude != "none") "${c.magnitude} change" else "within noise",
                if (improved) Green else if (worsened) Rose else TextMuted, Modifier.weight(1f))
            BigMetric("Outcome", if (improved) "improved" else if (worsened) "worsened" else "steady", if (c.reliable) "reliable" else "not reliable",
                if (improved) Green else if (worsened) Rose else TextMuted, Modifier.weight(1f))
        }
    }

    val cm = r.crossmodal
    val an = r.anomaly

    // 2) What each layer found — three InsightCards.
    GlassCard {
        Text("What each layer found", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        InsightCard(
            icon = if (improved) "↓" else if (worsened) "↑" else "→",
            tone = if (improved) Green else if (worsened) Rose else Cyan,
            title = "Stress change (Layer 3)",
            value = if (improved) "Reduced" else if (worsened) "Increased" else "No major change",
            text = if (c.reliable) "A ${fmt(kotlin.math.abs(c.delta))}-point shift, above the honest noise floor — a real change."
                   else "Below the reliable-change threshold given the model's confidence.",
        )
        val v = crossVerdict(cm)
        InsightCard(v.icon, v.tone, "Voice × heart (Layer 4)", v.short, crossModalPlain(cm))
        val goodAnom = an != null && (!an.anomaly || an.anomalyDirection == "unusual_improvement")
        InsightCard(
            icon = if (an == null) "○" else if (!an.anomaly) "✓" else if (an.anomalyDirection == "unusual_improvement") "★" else "!",
            tone = if (an == null) TextMuted else if (goodAnom) Green else Amber,
            title = "Session pattern (Layer 5)",
            value = if (an == null) "—" else if (!an.anomaly) "Pattern normal" else if (an.anomalyDirection == "unusual_improvement") "Exceptional improvement" else "Review suggested",
            text = anomalyPlain(an),
        )
    }

    // 3) Full cross-modal card (six cells + explanation), or an honest no-data note.
    CrossModalCard(cm)

    // 4) The valence/arousal circumplex (WP5) with the before→after arrow.
    if (vc.pre != null && vc.post != null) {
        GlassCard {
            Text("The signal behind the scores · valence & arousal", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            CircumplexPlot(vc.pre, vc.post)
        }
    }

    // 5) Every score, before and after (reasons hidden here).
    GlassCard {
        Text("Every score · before and after", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
    }
    vc.pre?.let { Text("Before", color = TextMuted, fontSize = 12.sp, fontWeight = FontWeight.Bold); StressResultCard(it, showReasons = false) }
    vc.post?.let { Text("After", color = TextMuted, fontSize = 12.sp, fontWeight = FontWeight.Bold); StressResultCard(it, showReasons = false) }

    // 6) What this means — baseline paragraph.
    GlassCard {
        Text("What this means", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        val base = r.personalBaseline
        val baseLine = if (base.personalised && base.relativeBand != null)
            " Compared with your own normal, this arrival reads as \"${base.relativeBand}\"."
        else " Your personal baseline is still being learned across sessions."
        Text("This is a wellbeing estimate, not a medical diagnosis. One session is a single data point;$baseLine Sessions are saved so patterns can be reviewed over time.",
            color = TextMuted, fontSize = 13.sp, lineHeight = 20.sp)
    }

    // 7) What might help — recommendation.
    GlassCard {
        Text("What might help", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        Text(recommendationPlain(c.direction, an, r.personalBaseline.personalised, vc.lowConfidenceCapture),
            color = TextPrimary, fontSize = 14.sp, lineHeight = 21.sp)
    }

    // 8) Technical detail — collapsed disclosure (supervisor / dissertation).
    TechnicalDetails(vc, r)

    GlassCard {
        Text("Thank you for talking with me today, ${vc.personName}. Whatever this reading says, showing up for yourself like this matters.",
            color = TextPrimary, fontSize = 14.sp, lineHeight = 21.sp)
    }
    Text("A wellbeing check-in, not a medical diagnosis.", color = TextMuted, fontSize = 11.sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
    PrimaryButton("Done") { onDone() }
}

// -------- WP6 report building blocks (ported from the web client) ----------

@Composable
private fun BigMetric(label: String, value: String, sub: String, tone: Color, modifier: Modifier = Modifier) {
    Column(
        modifier.background(Color(0x14FFFFFF), RoundedCornerShape(14.dp)).padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        Text(label, color = TextMuted, fontSize = 10.sp, fontWeight = FontWeight.SemiBold)
        Text(value, color = tone, fontSize = 20.sp, fontWeight = FontWeight.Bold)
        Text(sub, color = TextMuted, fontSize = 11.sp)
    }
}

@Composable
private fun InsightCard(icon: String, tone: Color, title: String, value: String, text: String) {
    Row(Modifier.fillMaxWidth().padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
        Box(Modifier.size(30.dp).background(tone.copy(alpha = 0.18f), CircleShape), contentAlignment = Alignment.Center) {
            Text(icon, color = tone, fontSize = 15.sp, fontWeight = FontWeight.Bold)
        }
        Column(Modifier.weight(1f)) {
            Text(title, color = TextMuted, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
            Text(value, color = tone, fontSize = 15.sp, fontWeight = FontWeight.Bold)
            Text(text, color = TextMuted, fontSize = 12.sp, lineHeight = 18.sp)
        }
    }
}

private class Verdict(val label: String, val tone: Color, val icon: String, val short: String)

private fun crossVerdict(cm: com.mindsyncvr.core.voice.CrossModalResult?): Verdict = when {
    cm == null -> Verdict("No heart data", TextMuted, "–", "No data")
    cm.lowConfidence -> Verdict("Voice uncertain — deferred to heart rate", Cyan, "≈", "Deferred")
    cm.validated -> Verdict("Voice and heart rate agree", Green, "✓", "Agree")
    else -> Verdict("Mismatch — ${(cm.mismatchType ?: "").replace('_', ' ')}", Amber, "≠", "Differ")
}

@Composable
private fun CrossModalCard(cm: com.mindsyncvr.core.voice.CrossModalResult?) {
    if (cm == null) {
        GlassCard {
            Text("Voice × heart rate · Layer 4", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            Text("Component B didn't report a heart-rate reading for this session, so this check-in is based on your voice alone.",
                color = TextMuted, fontSize = 13.sp, lineHeight = 20.sp)
        }
        return
    }
    val v = crossVerdict(cm)
    GlassCard {
        Text("Voice × heart rate · Layer 4 cross-modal", color = Teal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        Column(Modifier.fillMaxWidth().background(v.tone.copy(alpha = 0.10f), RoundedCornerShape(12.dp)).padding(12.dp)) {
            Text("${v.label}.", color = TextPrimary, fontSize = 13.sp, fontWeight = FontWeight.Bold)
            Text(crossModalPlain(cm), color = TextMuted, fontSize = 12.sp, lineHeight = 18.sp)
        }
        val cells = listOf(
            "Voice · before" to "${fmt(cm.voice.pre)} · conf ${fmt2(cm.voice.confidencePre)}",
            "Voice · after" to "${fmt(cm.voice.post)} · conf ${fmt2(cm.voice.confidencePost)}",
            "Heart · before" to fmt(cm.body.pre),
            "Heart · after" to fmt(cm.body.post),
            "Agreement" to fmt2(cm.agreement),
            "Verdict" to v.short + (cm.unresolvedMismatch?.let { " (${it.replace('_', ' ')})" } ?: ""),
        )
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            cells.chunked(2).forEach { rowCells ->
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    rowCells.forEach { (k, vv) ->
                        Column(Modifier.weight(1f).background(Color(0x14FFFFFF), RoundedCornerShape(12.dp)).padding(10.dp)) {
                            Text(k, color = TextMuted, fontSize = 10.sp)
                            Text(vv, color = TextPrimary, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }
        }
        Text("Voice carries valence reliably; heart-rate variability carries arousal. When the voice reading is uncertain, Layer 4 defers to the heart signal rather than asserting a mismatch — so an uncertain voice never raises a false alarm.",
            color = TextMuted, fontSize = 12.sp, lineHeight = 18.sp)
    }
}

@Composable
private fun TechnicalDetails(vc: VoiceCheckInState, r: com.mindsyncvr.core.voice.SessionReport) {
    var open by remember { mutableStateOf(false) }
    GlassCard {
        Row(Modifier.fillMaxWidth().clickable { open = !open }, horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Column {
                Text("Technical detail — every layer's raw output", color = TextPrimary, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                Text("All scores, for the study record. Hidden by default.", color = TextMuted, fontSize = 11.sp)
            }
            Text(if (open) "▲" else "▼", color = TextMuted, fontSize = 13.sp)
        }
        if (open) {
            TechPanel("Layer 2 — before", techVoice(vc.pre))
            TechPanel("Layer 2 — after", techVoice(vc.post))
            TechPanel("Layer 3 — comparison", "pre=${fmt(r.comparison.preStress)} post=${fmt(r.comparison.postStress)} delta=${fmt(r.comparison.delta)}\ndirection=${r.comparison.direction} magnitude=${r.comparison.magnitude} reliable=${r.comparison.reliable} meanConf=${fmt2(r.comparison.meanConfidence)}")
            TechPanel("Layer 4 — cross-modal", r.crossmodal?.let { "validated=${it.validated} agreement=${fmt2(it.agreement)} lowConfidence=${it.lowConfidence}\nmismatch=${it.mismatchType ?: "-"} deferredTo=${it.deferredTo ?: "-"}\nvoice pre/post=${fmt(it.voice.pre)}/${fmt(it.voice.post)} body pre/post=${fmt(it.body.pre)}/${fmt(it.body.post)}" } ?: "No heart data")
            TechPanel("Layer 5 — anomaly", r.anomaly?.let { "anomaly=${it.anomaly} direction=${it.anomalyDirection ?: "-"} severity=${it.severity}\nerror=${fmt3(it.error)} threshold=${fmt3(it.threshold)} personalised=${it.personalised}" } ?: "Anomaly model not loaded")
            TechPanel("Personal baseline", r.personalBaseline.let { "personalised=${it.personalised} baseline=${it.baseline ?: "-"} z=${it.z ?: "-"}\nband=${it.relativeBand ?: "-"} note=${it.note ?: "-"}" })
        }
    }
}

@Composable
private fun TechPanel(title: String, body: String) {
    Column(Modifier.fillMaxWidth().background(Color(0x14FFFFFF), RoundedCornerShape(10.dp)).padding(10.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
        Text(title, color = Cyan, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        Text(body, color = TextMuted, fontSize = 11.sp, lineHeight = 16.sp)
    }
}

private fun techVoice(a: com.mindsyncvr.core.voice.VoiceAnalysis?): String = a?.let {
    "score=${fmt(it.stressScore)} level=${it.stressLevel} type=${it.stressType ?: "-"}\nvalence=${fmt3(it.valence)} arousal=${fmt3(it.arousal)} confidence=${fmt3(it.confidence)} gate=${fmt3(it.gateMean)}"
} ?: "No reading"

private fun fmt2(d: Double) = String.format(Locale.US, "%.2f", d)
private fun fmt3(d: Double) = String.format(Locale.US, "%.3f", d)

private fun sessionSummaryLine(direction: String, magnitude: String) = when (direction) {
    "improved" -> "You sounded more at ease after the session than before — a ${magnitude.lowercase().ifBlank { "gentle" }} shift in the right direction."
    "worsened" -> "You sounded a little more tense afterwards. One session is just a snapshot — it doesn't define your day."
    else -> "Your stress stayed about the same this time. That's completely okay — calm isn't always a big change."
}

/** Layer 4 in plain language — mirrors the web client's mismatch explanations. */
private fun crossModalPlain(cm: com.mindsyncvr.core.voice.CrossModalResult?): String = when {
    cm == null -> "Your wristband wasn't connected this time, so this check-in is based on your voice alone. Wearing it next time adds how your body felt, for a fuller picture."
    cm.validated -> "Your voice and your body told the same story — that agreement makes this a reading you can trust."
    cm.lowConfidence -> "Your voice was hard to read clearly this time, so we leaned on your wristband instead. That's by design: when one signal is unsure, the other leads, so you never get a false alarm."
    cm.mismatchType == "vocal_masking" -> "Your voice sounded calmer than your body felt. Sometimes we hold it together in how we speak even while the body is still carrying tension."
    cm.mismatchType == "cognitive_persistence" -> "Your body settled, but your voice still carried some tension — your mind may still be working through the day even as your body relaxes."
    cm.mismatchType == "baseline_divergence" -> "Your voice and your body read a little differently through the session, so this comparison is less certain this time."
    cm.mismatchType == "outcome_divergence" -> "Your voice and your body agreed before the session but drifted apart afterwards."
    else -> "Your voice and your body didn't quite line up this time — worth gently noticing, not worrying about."
}

/** Layer 5 in plain language — session pattern vs the person's own history. */
private fun anomalyPlain(an: com.mindsyncvr.core.voice.AnomalyResult?): String = when {
    an == null -> "Once you've done a few sessions, we'll start learning your usual pattern and gently flag anything that stands out here."
    !an.anomaly -> "This session followed your usual pattern — nothing stood out. A steady, healthy sign."
    an.anomalyDirection == "unusual_improvement" -> "This session helped more than your usual — a genuinely strong result today. Whatever you did, it worked."
    else -> "This session looked different from your usual pattern. One off day is normal and not a concern on its own — but if it keeps happening, it's worth paying attention to."
}

private fun recommendationPlain(direction: String, an: com.mindsyncvr.core.voice.AnomalyResult?, personalised: Boolean, lowConfidence: Boolean): String {
    val base = when (direction) {
        "improved" -> "This practice is working for you — try keeping it as a regular part of your routine."
        "worsened" -> "Be gentle with yourself today. A longer calm session, some rest, or talking to someone you trust can all help."
        else -> "No big change this time, and that's fine — consistency matters far more than any single session."
    }
    val extra = when {
        an != null && an.anomaly && an.anomalyDirection != "unusual_improvement" ->
            " If today felt harder than usual, reaching out to someone can make a real difference."
        lowConfidence -> " Speaking a little more next time will also make your reading clearer."
        !personalised -> " A few more sessions will let this compare against your own normal."
        else -> ""
    }
    return base + extra
}

/** Plain stress-level word for a 0–10 score (uses the model's own label if present). */
private fun levelWord(level: String?, score: Double): String = when ((level ?: "").lowercase()) {
    "no" -> "No stress"
    "mild" -> "Mild"
    "moderate" -> "Moderate"
    "high" -> "High"
    else -> when {
        score < 2.5 -> "No stress"
        score < 5.0 -> "Mild"
        score < 7.5 -> "Moderate"
        else -> "High"
    }
}

private fun stageLabel(stage: VoiceStage) = when (stage) {
    VoiceStage.Intro -> "Welcome"
    VoiceStage.Environment -> "Step 1 · Environment"
    VoiceStage.PreConversation -> "Step 2 · Before session"
    VoiceStage.VrSession -> "Step 3 · Your session"
    VoiceStage.PostConversation -> "Step 4 · After session"
    VoiceStage.Report -> "Step 5 · Report"
}

private fun fmt(d: Double) = String.format(Locale.US, "%.1f", d)
