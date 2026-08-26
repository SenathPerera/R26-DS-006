@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.mindsyncvr.features.questionnaire

import androidx.compose.foundation.layout.*
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.MindSyncActions
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.model.*
import com.mindsyncvr.navigation.Routes

@Composable
fun QuestionnairesScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    val post = state.questionnaireTemplates.first { it.component == "component_d" }
    MindSyncScaffold {
        SectionHeader("Questionnaires", "Configurable pre-session, post-session, immersion, and adverse experience flows.")
        GlassCard {
            StatusPill("${state.pendingValidationCount} pending", if (state.pendingValidationCount > 0) Amber else Green)
            Text("Component D validation", color = TextPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
            Text("Responses are saved locally first and queued for backend sync with export-ready structure.", color = TextMuted, lineHeight = 22.sp)
        }
        QuestionnaireRenderer(template = post, submitLabel = "Save and queue validation") { answers ->
            actions.submitQuestionnaire(post.id, state.activeSession?.id ?: "latest-session", answers)
            navigate(Routes.QuestionnaireHistory)
        }
    }
}

@Composable
fun QuestionnaireHistoryScreen(state: AppState) {
    MindSyncScaffold {
        SectionHeader("Validation history", "Local-first responses and backend sync state.")
        if (state.questionnaireResponses.isEmpty()) {
            GlassCard {
                Text("No responses yet", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                Text("Post-session validation responses will appear here after completion.", color = TextMuted)
            }
        }
        state.questionnaireResponses.forEach { response ->
            GlassCard {
                StatusPill(if (response.synced) "Synced" else "Queued offline", if (response.synced) Green else Amber)
                Text(response.templateId, color = TextPrimary, fontWeight = FontWeight.Bold)
                Text("${response.submittedAt} · ${response.answers.size} answers · ${response.exportShapeVersion}", color = TextMuted)
            }
        }
    }
}

@Composable
fun QuestionnaireRenderer(template: QuestionnaireTemplate, submitLabel: String, onSubmit: (Map<String, String>) -> Unit) {
    var answers by remember(template.id) { mutableStateOf<Map<String, String>>(emptyMap()) }
    val visible = template.questions.filter { question ->
        question.branch?.let { branch ->
            val value = answers[branch.whenQuestionId]
            branch.equals == value || branch.includes?.let { value?.contains(it) } == true
        } ?: true
    }
    visible.forEach { question ->
        GlassCard {
            Text(question.prompt, color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 17.sp)
            when (question.type) {
                QuestionType.Text, QuestionType.VoiceNote -> {
                    OutlinedTextField(
                        value = answers[question.id] ?: "",
                        onValueChange = { answers = answers + (question.id to it) },
                        modifier = Modifier.fillMaxWidth().heightIn(min = 100.dp),
                        label = { Text(if (question.type == QuestionType.VoiceNote) "Voice note placeholder" else "Response") }
                    )
                }
                QuestionType.SingleChoice -> ChipOptions(question.options, answers[question.id]) { answers = answers + (question.id to it) }
                QuestionType.MultipleChoice -> ChipOptions(question.options, answers[question.id]?.split("|").orEmpty()) { selected ->
                    answers = answers + (question.id to selected.joinToString("|"))
                }
                QuestionType.Likert, QuestionType.Numeric, QuestionType.Slider -> {
                    val range = (question.min..question.max).map { it.toString() }
                    ChipOptions(range, answers[question.id]) { answers = answers + (question.id to it) }
                }
            }
        }
    }
    PrimaryButton(submitLabel) { onSubmit(answers) }
}

@Composable
private fun ChipOptions(options: List<String>, selected: String?, onSelect: (String) -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        options.forEach { option -> OptionChip(option, selected == option, onClick = { onSelect(option) }) }
    }
}

@Composable
private fun ChipOptions(options: List<String>, selected: List<String>, onSelect: (List<String>) -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        options.forEach { option ->
            OptionChip(option, selected.contains(option)) {
                onSelect(if (selected.contains(option)) selected - option else selected + option)
            }
        }
    }
}
