package com.mindsyncvr.core.bluetooth

import com.mindsyncvr.core.data.MockData
import com.mindsyncvr.core.model.ConnectionState
import com.mindsyncvr.core.model.RawPpgSample
import com.mindsyncvr.core.model.WearableDevice
import com.mindsyncvr.core.model.WearableTelemetry
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow

interface BleController {
    val state: StateFlow<ConnectionState>
    val ppgSamples: SharedFlow<List<RawPpgSample>>
    val telemetry: SharedFlow<WearableTelemetry>
    val logs: StateFlow<List<String>>
    val errors: StateFlow<String?>
    suspend fun scan(): List<WearableDevice>
    suspend fun connect(deviceId: String): WearableDevice
    suspend fun disconnect()
    suspend fun calibrate(deviceId: String): String
}

class MockBleController : BleController {
    private val mutableState = MutableStateFlow(ConnectionState.Idle)
    override val state: StateFlow<ConnectionState> = mutableState
    override val ppgSamples = MutableSharedFlow<List<RawPpgSample>>(extraBufferCapacity = 16)
    override val telemetry = MutableSharedFlow<WearableTelemetry>(extraBufferCapacity = 16)
    private val mutableLogs = MutableStateFlow<List<String>>(emptyList())
    override val logs: StateFlow<List<String>> = mutableLogs
    private val mutableErrors = MutableStateFlow<String?>(null)
    override val errors: StateFlow<String?> = mutableErrors

    override suspend fun scan(): List<WearableDevice> {
        log("Mock scan started")
        mutableState.value = ConnectionState.Scanning
        delay(900)
        mutableState.value = ConnectionState.Idle
        return MockData.devices
    }

    override suspend fun connect(deviceId: String): WearableDevice {
        log("Mock connect requested: $deviceId")
        mutableState.value = ConnectionState.Pairing
        delay(700)
        mutableState.value = ConnectionState.Connected
        return MockData.devices.firstOrNull { it.id == deviceId } ?: MockData.devices.first()
    }

    override suspend fun disconnect() {
        log("Mock disconnect")
        mutableState.value = ConnectionState.Disconnected
    }

    override suspend fun calibrate(deviceId: String): String {
        delay(800)
        return "Sensor window is stable. Calibration confidence is high for $deviceId."
    }

    private fun log(message: String) {
        mutableLogs.value = (listOf("BLE mock ${System.currentTimeMillis()}: $message") + mutableLogs.value).take(40)
    }
}
