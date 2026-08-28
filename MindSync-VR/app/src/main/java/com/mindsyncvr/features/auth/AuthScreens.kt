package com.mindsyncvr.features.auth

import androidx.compose.foundation.layout.*
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.MindSyncActions
import com.mindsyncvr.core.design.*

@Composable
fun WelcomeScreen(onLogin: () -> Unit, onSignUp: () -> Unit) {
    MindSyncScaffold {
        Spacer(Modifier.height(24.dp))
        Column(
            modifier = Modifier.fillMaxWidth(),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            BreathingOrb(132)
            Text("MindSync VR", color = TextPrimary, fontSize = 36.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
            CenterText("Adaptive VR meditation guided by physiological sensing, safe personalization, and post-session validation.")
        }
        GlassCard {
            Text("Research-grade wellness control hub", color = TextPrimary, fontSize = 22.sp, fontWeight = FontWeight.Bold)
            Text(
                "Pair your wearable, prepare the VR environment, begin a supported session, and complete Component D validation when you return.",
                color = TextMuted,
                fontSize = 16.sp,
                lineHeight = 24.sp
            )
            PrimaryButton("Log in", onClick = onLogin)
            SecondaryButton("Create account", onClick = onSignUp)
        }
    }
}

@Composable
fun LoginScreen(actions: MindSyncActions, onDone: () -> Unit) {
    var email by remember { mutableStateOf("participant@mindsync.local") }
    var password by remember { mutableStateOf("mindsync") }
    MindSyncScaffold {
        SectionHeader("Welcome back", "Sign in to continue your guided research session pathway.")
        GlassCard {
            Field("Email", email) { email = it }
            Field("Password", password, password = true) { password = it }
            PrimaryButton("Sign in") {
                actions.login(email, password)
                onDone()
            }
        }
    }
}

@Composable
fun SignUpScreen(actions: MindSyncActions, onDone: () -> Unit) {
    var name by remember { mutableStateOf("") }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    MindSyncScaffold {
        SectionHeader("Create your space", "Your data is treated as sensitive wellness research data.")
        GlassCard {
            Field("Preferred name", name) { name = it }
            Field("Email", email) { email = it }
            Field("Password", password, password = true) { password = it }
            PrimaryButton("Continue to onboarding") {
                actions.register(name.ifBlank { "Ari" }, email.ifBlank { "participant@mindsync.local" }, password.ifBlank { "mindsync-safe" })
                onDone()
            }
        }
    }
}

@Composable
private fun Field(label: String, value: String, password: Boolean = false, onValueChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(label) },
        singleLine = !password,
        visualTransformation = if (password) PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None
    )
}
