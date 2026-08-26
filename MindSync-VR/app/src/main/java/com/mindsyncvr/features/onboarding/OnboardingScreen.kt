@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.mindsyncvr.features.onboarding

import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.mindsyncvr.MindSyncActions
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.core.model.OnboardingProfile

@Composable
fun OnboardingScreen(state: AppState, actions: MindSyncActions, onDone: () -> Unit) {
    var profile by remember { mutableStateOf(state.onboarding) }
    val goals = listOf("Stress reduction", "Better focus", "Relaxation", "Sleep preparation", "Emotional balance")
    val audio = listOf("Nature heavy", "Soft drone", "Warm pads", "Subtle rhythm", "No vocals", "Neutral tone")
    val env = listOf("Forest", "Ocean", "Cave", "Mountain", "Abstract calm")
    val comfort = listOf("Avoid intense sounds", "Avoid sudden transitions", "Avoid darkness", "Motion sensitivity", "Low visual complexity")

    MindSyncScaffold {
        SectionHeader("Personalize gently", "Set safe defaults for meditation, audio, VR, and validation workflows.")
        GlassCard {
            SectionHeader("Meditation rhythm", "Your preferences can be changed later.")
            ChipGroup(listOf("New to meditation", "Occasional", "Weekly practice", "Experienced"), profile.meditationExperience) {
                profile = profile.copy(meditationExperience = it)
            }
            ChipGroup(listOf("8", "10", "15", "20", "30"), profile.preferredDuration.toString()) {
                profile = profile.copy(preferredDuration = it.toInt())
            }
        }
        PreferenceBlock("Goals", goals, profile.goals) { profile = profile.copy(goals = it) }
        PreferenceBlock("Audio comfort", audio, profile.audioPreferences) { profile = profile.copy(audioPreferences = it) }
        PreferenceBlock("VR environment", env, profile.environmentPreferences) { profile = profile.copy(environmentPreferences = it) }
        PreferenceBlock("Comfort boundaries", comfort, profile.sensitivities) { profile = profile.copy(sensitivities = it) }
        GlassCard {
            SectionHeader("Consent and privacy", "Physiological data is treated as sensitive wellness research data.")
            OptionChip("Privacy acknowledged", profile.consentAccepted) { profile = profile.copy(consentAccepted = !profile.consentAccepted) }
            OptionChip("Research participation consent", profile.researchConsent) { profile = profile.copy(researchConsent = !profile.researchConsent) }
        }
        PrimaryButton("Complete onboarding") {
            actions.updateOnboarding(profile.ensureDefaults())
            actions.completeOnboarding()
            onDone()
        }
    }
}

@Composable
private fun PreferenceBlock(title: String, options: List<String>, selected: List<String>, onChange: (List<String>) -> Unit) {
    GlassCard {
        SectionHeader(title)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            options.forEach { option ->
                OptionChip(option, selected.contains(option)) {
                    onChange(if (selected.contains(option)) selected - option else selected + option)
                }
            }
        }
    }
}

@Composable
private fun ChipGroup(options: List<String>, selected: String, onSelect: (String) -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        options.forEach { option -> OptionChip(option, selected == option, onClick = { onSelect(option) }) }
    }
}

private fun OnboardingProfile.ensureDefaults(): OnboardingProfile = copy(
    meditationExperience = meditationExperience.ifBlank { "Occasional" },
    goals = goals.ifEmpty { listOf("Stress reduction", "Relaxation") },
    audioPreferences = audioPreferences.ifEmpty { listOf("Warm pads", "No vocals") },
    environmentPreferences = environmentPreferences.ifEmpty { listOf("Ocean", "Abstract calm") },
    sensitivities = sensitivities.ifEmpty { listOf("Avoid sudden transitions", "Motion sensitivity") },
    consentAccepted = true
)
