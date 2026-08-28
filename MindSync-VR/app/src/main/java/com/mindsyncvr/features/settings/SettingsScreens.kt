package com.mindsyncvr.features.settings

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import com.mindsyncvr.core.design.*
import com.mindsyncvr.navigation.Routes

@Composable
fun SettingsScreen(navigate: (String) -> Unit) {
    MindSyncScaffold {
        SectionHeader("Settings", "Manage account, privacy, integrations, appearance, language, and support.")
        SettingCard("Account", "Email, password, verification, and session security.")
        SettingCard("Privacy", "Consent, export, deletion, and sensitive data controls.")
        SettingCard("Wearable management", "BLE device permissions, calibration, and signal quality.")
        SettingCard("VR and Unity", "Unity Android Library embedding, pairing, heartbeat, and session handoff.")
        GlassCard {
            Text("Support", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            Text("Help, troubleshooting, and discomfort escalation guidance.", color = TextMuted)
            SecondaryButton("Open support") { navigate(Routes.Support) }
        }
        SettingCard("About research study", "Component A/B/C/D/E architecture and app version.")
    }
}

@Composable
fun SupportScreen(navigate: (String) -> Unit) {
    MindSyncScaffold {
        SectionHeader("Support", "Gentle help paths for setup, safety, and study contact.")
        GlassCard {
            Text("Feeling discomfort?", color = TextPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
            Text("Stop the session, remove the headset, orient to the room, and report what happened when ready.", color = TextMuted, lineHeight = 22.sp)
            SecondaryButton("Open discomfort report", danger = true) { navigate(Routes.Questionnaires) }
        }
        SettingCard("BLE troubleshooting", "Keep the wearable close, check sensor contact, and recalibrate if confidence drops.")
        SettingCard("Unity troubleshooting", "Confirm the exported Unity library is attached and lifecycle events are forwarded through UnityBridge.")
    }
}

@Composable
private fun SettingCard(title: String, body: String) {
    GlassCard {
        Text(title, color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
        Text(body, color = TextMuted, lineHeight = 22.sp)
    }
}
