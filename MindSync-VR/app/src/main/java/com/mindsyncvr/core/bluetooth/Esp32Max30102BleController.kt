package com.mindsyncvr.core.bluetooth

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.ParcelUuid
import android.util.Log
import androidx.core.content.ContextCompat
import com.mindsyncvr.core.model.ConnectionState
import com.mindsyncvr.core.model.RawPpgSample
import com.mindsyncvr.core.model.WearableDevice
import com.mindsyncvr.core.model.WearableTelemetry
import kotlinx.serialization.json.Json
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeoutOrNull
import java.util.UUID
import kotlin.coroutines.resume

class WearableHealthBleController(
    context: Context
) : BleController {
    private val appContext = context.applicationContext
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val bluetoothManager = appContext.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager
    private val bluetoothAdapter: BluetoothAdapter? = bluetoothManager.adapter
    private val scanner get() = bluetoothAdapter?.bluetoothLeScanner

    private val mutableState = MutableStateFlow(ConnectionState.Idle)
    override val state: StateFlow<ConnectionState> = mutableState

    private val mutableSamples = MutableSharedFlow<List<RawPpgSample>>(extraBufferCapacity = 64)
    override val ppgSamples: SharedFlow<List<RawPpgSample>> = mutableSamples
    private val mutableTelemetry = MutableSharedFlow<WearableTelemetry>(extraBufferCapacity = 64)
    override val telemetry: SharedFlow<WearableTelemetry> = mutableTelemetry

    private val mutableLogs = MutableStateFlow<List<String>>(emptyList())
    override val logs: StateFlow<List<String>> = mutableLogs
    private val mutableErrors = MutableStateFlow<String?>(null)
    override val errors: StateFlow<String?> = mutableErrors

    private var gatt: BluetoothGatt? = null
    private var connectedDevice: BluetoothDevice? = null
    private var reconnectEnabled = false
    private var lastDeviceAddress: String? = null

    override suspend fun scan(): List<WearableDevice> {
        ensureBluetoothReady()
        log("Starting BLE scan for $DEVICE_NAME with service filter $SERVICE_UUID")
        mutableState.value = ConnectionState.Scanning

        val result = withTimeoutOrNull(SCAN_TIMEOUT_MS) {
            scanResults(useServiceFilter = true).first()
        } ?: withTimeoutOrNull(FALLBACK_SCAN_TIMEOUT_MS) {
            log("Service-filtered scan found nothing; running short fallback name scan", warn = true)
            scanResults(useServiceFilter = false).first()
        }

        mutableState.value = ConnectionState.Idle

        if (result == null) {
            val message = "Device not found: $DEVICE_NAME"
            log(message, warn = true)
            log("Confirm the ESP32 is advertising service $SERVICE_UUID and name $DEVICE_NAME", warn = true)
            mutableErrors.value = message
            return emptyList()
        }

        log("Found $DEVICE_NAME at ${result.device.address}, RSSI=${result.rssi}")
        return listOf(result.toWearableDevice())
    }

    override suspend fun connect(deviceId: String): WearableDevice {
        ensureBluetoothReady()
        reconnectEnabled = true
        lastDeviceAddress = deviceId
        mutableState.value = ConnectionState.Pairing
        log("Connecting to wearable telemetry device: $deviceId")

        val device = bluetoothAdapter?.getRemoteDevice(deviceId)
            ?: throw IllegalStateException("Bluetooth adapter unavailable")

        connectedDevice = device
        connectGatt(device)
        return WearableDevice(
            id = device.address,
            name = DEVICE_NAME,
            rssi = 0,
            battery = 0,
            firmware = "ESP32-S3 Mini",
            lastSync = "Live",
            signalQuality = 0,
            confidence = 0
        )
    }

    @SuppressLint("MissingPermission")
    override suspend fun disconnect() {
        reconnectEnabled = false
        log("Disconnect requested")
        gatt?.disconnect()
        gatt?.close()
        gatt = null
        connectedDevice = null
        mutableState.value = ConnectionState.Disconnected
    }

    override suspend fun calibrate(deviceId: String): String {
        return "MAX30100 and INMP441 setup is performed on the ESP32 firmware. BLE telemetry streaming is ${state.value}."
    }

    @SuppressLint("MissingPermission")
    private fun scanResults(useServiceFilter: Boolean): Flow<ScanResult> = callbackFlow {
        val seenAddresses = mutableSetOf<String>()
        val callback = object : ScanCallback() {
            override fun onScanResult(callbackType: Int, result: ScanResult) {
                val advertisedName = result.scanRecord?.deviceName ?: result.device.name
                val hasService = result.scanRecord?.serviceUuids?.contains(ParcelUuid(SERVICE_UUID)) == true
                val address = result.device.address

                if (seenAddresses.add(address)) {
                    val serviceText = result.scanRecord?.serviceUuids?.joinToString { it.uuid.toString() }.orEmpty()
                    log("Saw BLE device name=${advertisedName ?: "<no name>"} address=$address rssi=${result.rssi} services=$serviceText")
                }

                if (isTargetAdvertisement(advertisedName, hasService)) {
                    trySend(result)
                }
            }

            override fun onScanFailed(errorCode: Int) {
                val message = "BLE scan failed with code $errorCode"
                log(message, warn = true)
                mutableErrors.value = message
                mutableState.value = ConnectionState.Error
                close(IllegalStateException(message))
            }
        }

        val settings = ScanSettings.Builder()
            .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
            .build()
        val filters = if (useServiceFilter) {
            listOf(ScanFilter.Builder().setServiceUuid(ParcelUuid(SERVICE_UUID)).build())
        } else {
            null
        }

        scanner?.startScan(filters, settings, callback)
        awaitClose {
            scanner?.stopScan(callback)
        }
    }

    private fun isTargetAdvertisement(advertisedName: String?, hasService: Boolean): Boolean {
        val normalized = advertisedName?.trim().orEmpty()
        return hasService ||
            normalized == DEVICE_NAME ||
            normalized.contains("WearableHealth", ignoreCase = true)
    }

    @SuppressLint("MissingPermission")
    private suspend fun connectGatt(device: BluetoothDevice) {
        suspendCancellableCoroutine { continuation ->
            val callback = object : BluetoothGattCallback() {
                override fun onConnectionStateChange(gatt: BluetoothGatt, status: Int, newState: Int) {
                    when (newState) {
                        BluetoothProfile.STATE_CONNECTED -> {
                            log("GATT connected; requesting MTU $REQUESTED_MTU")
                            mutableState.value = ConnectionState.Connected
                            val mtuRequested = gatt.requestMtu(REQUESTED_MTU)
                            if (!mtuRequested) {
                                log("MTU request returned false; discovering services with default MTU", warn = true)
                                gatt.discoverServices()
                            } else {
                                scheduleMtuFallbackDiscovery(gatt)
                            }
                            if (continuation.isActive) continuation.resume(Unit)
                        }

                        BluetoothProfile.STATE_DISCONNECTED -> {
                            log("GATT disconnected status=$status", warn = status != BluetoothGatt.GATT_SUCCESS)
                            mutableState.value = ConnectionState.Disconnected
                            gatt.close()
                            if (reconnectEnabled) scheduleReconnect()
                            if (continuation.isActive) continuation.resume(Unit)
                        }
                    }
                }

                override fun onMtuChanged(gatt: BluetoothGatt, mtu: Int, status: Int) {
                    log("MTU changed mtu=$mtu status=$status; discovering services")
                    gatt.discoverServices()
                }

                override fun onServicesDiscovered(gatt: BluetoothGatt, status: Int) {
                    if (status != BluetoothGatt.GATT_SUCCESS) {
                        log("Service discovery failed status=$status", warn = true)
                        mutableErrors.value = "Service discovery failed status=$status"
                        mutableState.value = ConnectionState.Error
                        return
                    }

                    val telemetryCharacteristic = gatt
                        .getService(SERVICE_UUID)
                        ?.getCharacteristic(TELEMETRY_CHARACTERISTIC_UUID)

                    if (telemetryCharacteristic == null) {
                        log("Telemetry characteristic not found", warn = true)
                        mutableErrors.value = "Telemetry characteristic not found"
                        mutableState.value = ConnectionState.Error
                        return
                    }

                    subscribeToTelemetry(gatt, telemetryCharacteristic)
                }

                @Deprecated("Deprecated in Java")
                @Suppress("DEPRECATION")
                override fun onCharacteristicChanged(gatt: BluetoothGatt, characteristic: BluetoothGattCharacteristic) {
                    if (characteristic.uuid == TELEMETRY_CHARACTERISTIC_UUID) {
                        handleTelemetryNotification(characteristic.value)
                    }
                }

                override fun onCharacteristicChanged(
                    gatt: BluetoothGatt,
                    characteristic: BluetoothGattCharacteristic,
                    value: ByteArray
                ) {
                    if (characteristic.uuid == TELEMETRY_CHARACTERISTIC_UUID) {
                        handleTelemetryNotification(value)
                    }
                }
            }

            gatt?.close()
            gatt = device.connectGatt(appContext, false, callback, BluetoothDevice.TRANSPORT_LE)
            continuation.invokeOnCancellation {
                gatt?.disconnect()
            }
        }
    }

    @SuppressLint("MissingPermission")
    private fun subscribeToTelemetry(gatt: BluetoothGatt, characteristic: BluetoothGattCharacteristic) {
        log("Subscribing to telemetry notifications: $TELEMETRY_CHARACTERISTIC_UUID")
        val enabled = gatt.setCharacteristicNotification(characteristic, true)
        if (!enabled) {
            log("setCharacteristicNotification returned false", warn = true)
            mutableErrors.value = "Unable to enable telemetry notifications"
            mutableState.value = ConnectionState.Error
            return
        }

        val descriptor = characteristic.getDescriptor(CLIENT_CHARACTERISTIC_CONFIG_UUID)
        if (descriptor == null) {
            log("CCCD descriptor missing for telemetry characteristic", warn = true)
            mutableErrors.value = "CCCD descriptor missing for telemetry characteristic"
            mutableState.value = ConnectionState.Error
            return
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            gatt.writeDescriptor(descriptor, BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE)
        } else {
            @Suppress("DEPRECATION")
            run {
                descriptor.value = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
                gatt.writeDescriptor(descriptor)
            }
        }
        log("Telemetry subscription requested")
    }

    private fun handleTelemetryNotification(payload: ByteArray) {
        val jsonText = payload.toString(Charsets.UTF_8).trim()
        if (jsonText.isBlank()) {
            val message = "Malformed telemetry packet: empty payload"
            log(message, warn = true)
            mutableErrors.value = message
            return
        }

        runCatching {
            json.decodeFromString(WearableTelemetry.serializer(), jsonText)
        }.onSuccess { telemetry ->
            if (telemetry.ir == null || telemetry.red == null || telemetry.noiseAverage == null || telemetry.noisePeak == null) {
                val message = "Malformed telemetry packet: missing required fields"
                log("$message payload=$jsonText", warn = true)
                mutableErrors.value = message
                return
            }
            mutableTelemetry.tryEmit(telemetry)
            mutableErrors.value = null
            log("Telemetry decoded ir=${telemetry.ir} red=${telemetry.red} noiseAvg=${telemetry.noiseAverage} noisePeak=${telemetry.noisePeak}")
        }.onFailure { error ->
            val message = "Malformed telemetry packet: ${error.message}"
            log("$message payload=$jsonText", warn = true)
            mutableErrors.value = message
        }
    }

    @SuppressLint("MissingPermission")
    private fun scheduleMtuFallbackDiscovery(gatt: BluetoothGatt) {
        scope.launch {
            delay(MTU_FALLBACK_DISCOVERY_MS)
            if (state.value == ConnectionState.Connected) {
                log("MTU callback fallback elapsed; discovering services")
                gatt.discoverServices()
            }
        }
    }

    @SuppressLint("MissingPermission")
    private fun scheduleReconnect() {
        val address = lastDeviceAddress ?: return
        scope.launch {
            delay(RECONNECT_DELAY_MS)
            if (!reconnectEnabled) return@launch
            log("Attempting BLE reconnect to $address")
            runCatching { connect(address) }
                .onFailure {
                    log("Reconnect failed: ${it.message}", warn = true)
                    mutableErrors.value = "Reconnect failed: ${it.message}"
                    mutableState.value = ConnectionState.Error
                    scheduleReconnect()
                }
        }
    }

    private fun ensureBluetoothReady() {
        if (bluetoothAdapter == null) error("Bluetooth is not available on this device")
        if (bluetoothAdapter?.isEnabled != true) error("Bluetooth is disabled")
        if (!hasRequiredPermissions()) error("Bluetooth permission not granted")
    }

    private fun hasRequiredPermissions(): Boolean {
        val permissions = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            listOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            listOf(Manifest.permission.ACCESS_FINE_LOCATION)
        }
        return permissions.all {
            ContextCompat.checkSelfPermission(appContext, it) == PackageManager.PERMISSION_GRANTED
        }
    }

    private fun log(message: String, warn: Boolean = false) {
        val line = "BLE ${System.currentTimeMillis()}: $message"
        if (warn) Log.w(TAG, message) else Log.d(TAG, message)
        if (!warn && mutableErrors.value == message) mutableErrors.value = null
        mutableLogs.update { logs -> (listOf(line) + logs).take(MAX_LOG_LINES) }
    }

    @SuppressLint("MissingPermission")
    private fun ScanResult.toWearableDevice(): WearableDevice {
        val advertisedName = scanRecord?.deviceName ?: device.name ?: DEVICE_NAME
        return WearableDevice(
            id = device.address,
            name = advertisedName,
            rssi = rssi,
            battery = 0,
            firmware = "ESP32-S3 Mini MAX30100 + INMP441",
            lastSync = "Discovered",
            signalQuality = 0,
            confidence = 0
        )
    }
    companion object {
        private const val TAG = "WearableHealthBle"
        private const val DEVICE_NAME = "WearableHealthMonitor"
        private const val SCAN_TIMEOUT_MS = 12_000L
        private const val FALLBACK_SCAN_TIMEOUT_MS = 6_000L
        private const val RECONNECT_DELAY_MS = 2_000L
        private const val MTU_FALLBACK_DISCOVERY_MS = 1_200L
        private const val MAX_LOG_LINES = 80
        private const val REQUESTED_MTU = 185

        private val SERVICE_UUID: UUID = UUID.fromString("9f2d7a10-9c1b-4f3d-8a6e-7b35e2a10000")
        private val TELEMETRY_CHARACTERISTIC_UUID: UUID = UUID.fromString("9f2d7a11-9c1b-4f3d-8a6e-7b35e2a10000")
        private val CLIENT_CHARACTERISTIC_CONFIG_UUID: UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb")
        private val json = Json {
            ignoreUnknownKeys = true
            isLenient = true
        }
    }
}
