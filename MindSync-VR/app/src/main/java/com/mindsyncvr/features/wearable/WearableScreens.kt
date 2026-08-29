package com.mindsyncvr.features.wearable

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mindsyncvr.MindSyncActions
import com.mindsyncvr.core.design.*
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.core.model.ConnectionState
import com.mindsyncvr.navigation.Routes
import java.util.Locale

@Composable
fun WearableScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    val context = LocalContext.current
    val permissions = remember { requiredBlePermissions() }
    val launcher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestMultiplePermissions()
    ) { result ->
        if (permissions.all { result[it] == true || hasPermission(context, it) }) {
            actions.scanWearables()
        }
    }

    MindSyncScaffold {
        SectionHeader("Connect wearable", "Scan for BLE physiological sensors and select the research device for this session.")
        GlassCard {
            Text("ESP32-S3 target", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            Text("Device name: WearableHealthMonitor", color = TextMuted)
            Text("Service: 7c69f001-7f70-4b0a-9c91-93d7f91b1001", color = TextMuted, fontSize = 12.sp)
            Text("Telemetry characteristic: 7c69f002-7f70-4b0a-9c91-93d7f91b1001", color = TextMuted, fontSize = 12.sp)
            Text("Telemetry: JSON notifications at about 5 Hz", color = TextMuted, fontSize = 12.sp)
        }
        PrimaryButton(if (state.wearableState == ConnectionState.Scanning) "Scanning..." else "Scan wearable") {
            if (permissions.all { hasPermission(context, it) }) {
                actions.scanWearables()
            } else {
                launcher.launch(permissions)
            }
        }
        if (state.wearableDevices.isNotEmpty()) {
            SectionHeader("Nearby candidates", "Connect the strongest nearby device if the ESP32 name is hidden.")
            state.wearableDevices.forEachIndexed { index, device ->
                GlassCard {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Column(Modifier.weight(1f)) {
                            Text(
                                if (index == 0) "${device.name} · strongest" else device.name,
                                color = TextPrimary,
                                fontSize = 18.sp,
                                fontWeight = FontWeight.Bold
                            )
                            Text("Address ${device.id}", color = TextMuted, fontSize = 12.sp)
                            Text("RSSI ${device.rssi} dBm · ${device.firmware}", color = TextMuted)
                        }
                        StatusPill("${device.rssi}", if (device.rssi > -70) Green else Amber)
                    }
                    SecondaryButton("Connect and verify") {
                        actions.connectWearable(device.id)
                        navigate(Routes.WearableDetail)
                    }
                }
            }
        }
        state.bleIngestion.lastError?.let { error ->
            GlassCard {
                Text("BLE issue", color = Danger, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                Text(error, color = TextMuted)
            }
        }
        GlassCard {
            Text("Scan logs", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            state.bleIngestion.logs.take(10).forEach { logLine ->
                Text(logLine, color = TextMuted, fontSize = 11.sp)
            }
            if (state.bleIngestion.logs.isEmpty()) {
                Text("Tap scan to see nearby BLE advertisements.", color = TextMuted)
            }
        }
    }
}

@Composable
fun WearableDetailScreen(state: AppState, actions: MindSyncActions, navigate: (String) -> Unit) {
    val device = state.selectedWearable
    val telemetry = state.bleIngestion.latestTelemetry

    MindSyncScaffold {
        SectionHeader("Wearable detail", "Sensor readiness, streaming quality, and device metadata.")
        GlassCard {
            Text(device?.name ?: "No wearable connected", color = TextPrimary, fontSize = 24.sp, fontWeight = FontWeight.Bold)
            StatusPill(state.wearableState.name, if (state.wearableState == ConnectionState.Connected) Green else Amber)
            Text("Firmware ${device?.firmware ?: "unknown"} · Identifier ${device?.id ?: "n/a"}", color = TextMuted)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                ProgressRing(if (telemetry?.ir != null) 100 else 0, "PPG")
                ProgressRing(if (telemetry?.noiseAverage != null) 100 else 0, "noise")
                ProgressRing(telemetry?.batteryPercent ?: 0, "battery")
            }
            Text("Data streaming status: ${if (state.wearableState == ConnectionState.Connected) "Live relay ready" else "Waiting for connection"}", color = TextMuted)
        }
        GlassCard {
            Text("Live wearable telemetry", color = TextPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
            StatusPill(if (state.bleIngestion.isStreaming) "Streaming" else "Waiting", if (state.bleIngestion.isStreaming) Green else Amber)
            Text("IR: ${telemetry?.ir ?: "-"}", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            Text("RED: ${telemetry?.red ?: "-"}", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            Text("Noise average: ${telemetry?.noiseAverage ?: "-"}", color = TextMuted)
            Text("Noise peak: ${telemetry?.noisePeak ?: "-"}", color = TextMuted)
            Text("Device timestamp: ${telemetry?.timestampMs ?: "-"} ms", color = TextMuted)
            Text("Telemetry packets: ${state.bleIngestion.telemetryCount}", color = TextMuted)
        }
        GlassCard {
            Text("Future computed values", color = TextPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
            Text("Heart rate: ${telemetry?.heartRateBpm?.let { String.format(Locale.US, "%.1f bpm", it) } ?: "Not implemented on firmware"}", color = TextMuted)
            Text("RR interval: ${telemetry?.rrIntervalMs?.let { "$it ms" } ?: "Not implemented on firmware"}", color = TextMuted)
            Text("SpO2: ${telemetry?.spo2?.let { String.format(Locale.US, "%.1f%%", it) } ?: "Not implemented on firmware"}", color = TextMuted)
            Text("Temperature: ${telemetry?.temperatureC?.let { String.format(Locale.US, "%.1f C", it) } ?: "Not connected"}", color = TextMuted)
            Text("Battery: ${telemetry?.batteryPercent?.let { "$it%" } ?: "Not implemented on firmware"}", color = TextMuted)
        }
        GlassCard {
            Text("Raw PPG ingestion", color = TextPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
            Text("Latest timestamp: ${state.bleIngestion.latestSample?.timestampMs ?: "-"} ms", color = TextMuted)
            Text("Latest IR value: ${state.bleIngestion.latestSample?.irValue ?: "-"}", color = TextMuted)
            Text("IR samples received: ${state.bleIngestion.sampleCount}", color = TextMuted)
            state.bleIngestion.lastError?.let { Text("Last error: $it", color = Danger) }
        }
        GlassCard {
            Text("Recent BLE lifecycle logs", color = TextPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            state.bleIngestion.logs.take(8).forEach { logLine ->
                Text(logLine, color = TextMuted, fontSize = 11.sp)
            }
            if (state.bleIngestion.logs.isEmpty()) {
                Text("No BLE logs yet.", color = TextMuted)
            }
        }
        SecondaryButton("Disconnect wearable", danger = true) { actions.disconnectWearable() }
        SecondaryButton("Troubleshooting") { navigate(Routes.Support) }
        SecondaryButton("Back to dashboard") { navigate(Routes.Home) }
    }
}

private fun requiredBlePermissions(): Array<String> {
    return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        arrayOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)
    } else {
        arrayOf(Manifest.permission.ACCESS_COARSE_LOCATION, Manifest.permission.ACCESS_FINE_LOCATION)
    }
}

private fun hasPermission(context: android.content.Context, permission: String): Boolean {
    return androidx.core.content.ContextCompat.checkSelfPermission(
        context,
        permission
    ) == android.content.pm.PackageManager.PERMISSION_GRANTED
}
