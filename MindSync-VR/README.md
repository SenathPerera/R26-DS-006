# MindSync VR Native Android

MindSync VR is now a **native Android Kotlin** application for the AI-Based Adaptive VR Meditation System. The app is designed as the central orchestration layer between:

- wearable physiological sensing over BLE
- backend/cloud APIs
- Unity-based VR meditation experience
- onboarding and personalization
- live session control and monitoring
- Component D post-session validation
- session history, trends, and research reports

The previous React Native/Expo implementation has been replaced with a Kotlin + Jetpack Compose project so Unity embedding, Android lifecycle coordination, BLE permissions, and native VR handoff are much easier to manage.

## Stack

- Kotlin
- Native Android
- Jetpack Compose + Material 3
- MVVM-style `ViewModel`
- Kotlin Coroutines + Flow
- AndroidX Navigation Compose
- DataStore-ready secure preference layer
- Retrofit/OkHttp-ready API boundary
- Nordic BLE dependency for production BLE implementation
- Unity bridge abstraction for Unity Android Library embedding

## Project Structure

```text
app/src/main/java/com/mindsyncvr/
  MainActivity.kt
  MindSyncViewModel.kt
  core/
    bluetooth/      BLE controller abstraction and mock
    data/           repository and mock research data
    design/         Compose theme and reusable UI components
    model/          strongly typed domain and questionnaire models
    network/        API client abstraction
    realtime/       live session stream abstraction
    storage/        DataStore-backed storage helper
    unity/          UnityBridge abstraction
  features/
    auth/
    onboarding/
    dashboard/
    wearable/
    vr/
    session/
    questionnaire/
    analytics/
    settings/
  navigation/
```

## Run

Open this folder in Android Studio:

```text
/Users/sarindusamarasekara/Desktop/Mobile-App/MindSync-VR
```

Then sync Gradle and run the `app` configuration.

CLI build:

```bash
./gradlew :app:assembleDebug
```

If the CLI says Android SDK location is missing, create `local.properties`:

```properties
sdk.dir=/Users/<your-user>/Library/Android/sdk
```

or set:

```bash
export ANDROID_HOME=/Users/<your-user>/Library/Android/sdk
```

## Unity Embedding

Export the Unity VR project as an **Android Library** from Unity, then add it beside `app/` as `unityLibrary/`.

Update `settings.gradle.kts`:

```kotlin
include(":unityLibrary")
```

Then add this to `app/build.gradle.kts`:

```kotlin
implementation(project(":unityLibrary"))
```

The integration point is:

```text
core/unity/UnityBridge.kt
```

Replace `MockUnityBridge` with a real implementation that owns Unity lifecycle calls, creates or attaches the Unity view, and sends messages such as:

- session ID
- onboarding preferences
- environment profile
- audio profile
- live stress band
- signal confidence
- pause / resume / stop
- discomfort report events

This keeps Unity out of the app's wellness/business logic and makes the phone app the controller.

## BLE Integration

The BLE boundary is:

```text
core/bluetooth/BleController.kt
```

The app includes a production Android BLE ingestion path for the ESP32-S3 Mini wearable with MAX30100 PPG and INMP441 environmental noise telemetry:

```text
core/bluetooth/Esp32Max30102BleController.kt
core/model/DomainModels.kt
features/wearable/WearableScreens.kt
```

Expected BLE contract:

```text
Device name: WearableHealthMonitor
Service UUID: 7c69f001-7f70-4b0a-9c91-93d7f91b1001
Telemetry characteristic UUID: 7c69f002-7f70-4b0a-9c91-93d7f91b1001
Characteristic properties: READ + NOTIFY
Notification frequency: approximately 5 Hz
```

Telemetry notifications use compact UTF-8 JSON so first-stage hardware debugging is readable while staying under practical BLE MTU limits:

```json
{"ir":24500,"red":43000,"noiseAvg":85000,"noisePeak":180000}
```

Field meanings:

- `ir`: MAX30100 IR reading
- `red`: MAX30100 RED reading
- `hr`: heart rate BPM, currently `null` until implemented on firmware
- `rr`: RR interval in milliseconds, currently `null`
- `spo2`: oxygen saturation percentage, currently `null`
- `noiseAvg`: INMP441 noise average magnitude
- `noisePeak`: INMP441 noise peak magnitude
- `temp`: temperature in Celsius, currently `null`
- `bat`: battery percentage, currently `null`
- `flags`: bitmask for sensor/status faults; bit `0` = MAX30100 read issue, bit `1` = microphone issue

Malformed or incomplete packets are rejected and surfaced through BLE logs/errors. The production path does not fabricate heart rate, RR, SpO2, temperature, or battery percentage.

To test:

1. Flash/run the ESP32-S3 firmware in `firmware/esp32_s3_wearable_health_monitor/`.
2. Open the app.
3. Go to Dashboard -> Connect wearable.
4. Grant Bluetooth permissions.
5. Tap scan, then connect to `WearableHealthMonitor`.
6. Open the wearable detail/debug panel to see IR, RED, noise average, noise peak, telemetry count, and BLE lifecycle logs.

Production BLE should handle:

- runtime permissions for Android 12+
- scanning filters
- auto reconnect
- signal quality
- battery characteristic
- sensor readiness
- local buffering
- privacy-aware metadata relay

## ESP32-S3 Firmware

Arduino sketch:

```text
firmware/esp32_s3_wearable_health_monitor/esp32_s3_wearable_health_monitor.ino
```

Confirmed wiring used by the sketch:

```text
MAX30100 VIN -> 3V3
MAX30100 GND -> GND
MAX30100 SDA -> GPIO1
MAX30100 SCL -> GPIO2

INMP441 VDD -> 3V3
INMP441 GND -> GND
INMP441 SCK -> GPIO6
INMP441 WS  -> GPIO7
INMP441 SD  -> GPIO8
INMP441 L/R -> GND
```

Arduino IDE setup:

1. Install Arduino IDE 2.x.
2. Add Espressif board package URL in Preferences:

```text
https://raw.githubusercontent.com/espressif/arduino-esp32/gh-pages/package_esp32_index.json
```

3. Install `esp32` by Espressif Systems from Boards Manager.
4. Select the ESP32-S3 Mini compatible board profile. If there is no exact Mini profile, use `ESP32S3 Dev Module`.
5. Install/select these built-in or board-provided libraries: `Wire`, `driver/i2s`, and ESP32 `BLE`.
6. Open `firmware/esp32_s3_wearable_health_monitor/esp32_s3_wearable_health_monitor.ino`.
7. Set Serial Monitor to `115200`.
8. Upload.

First-stage hardware test:

1. Power on ESP32-S3 wearable.
2. Confirm Serial shows `WearableHealthMonitor ESP32-S3 Mini starting`.
3. Open mobile app.
4. Tap `Scan wearable`.
5. App finds `WearableHealthMonitor`.
6. Tap `Connect`.
7. Connection shows `Connected`.
8. Put finger on MAX30100.
9. IR and RED values change live in the app.
10. Speak or clap near INMP441.
11. Noise Average and Noise Peak values change live in the app.
12. Turn wearable off.
13. App detects disconnection gracefully.
14. Turn wearable back on.
15. Tap scan/connect again, or wait for reconnect if the previous connection is still active.

## Backend Integration

The API boundary is:

```text
core/network/ApiClient.kt
core/data/MindSyncRepository.kt
```

Suggested endpoint groups:

- `/auth`
- `/user`
- `/wearable`
- `/vr`
- `/sessions`
- `/questionnaires`
- `/analytics`
- `/realtime`

The repository is intentionally mock-backed first so the app can run before real backend, wearable, and Unity hardware are connected.

## Component D Validation

Component D is explicitly modeled through:

```text
core/model/DomainModels.kt
features/questionnaire/QuestionnaireScreens.kt
core/data/MockData.kt
```

It supports:

- configurable templates
- single choice
- multiple choice
- Likert
- slider/numeric
- text
- future voice note placeholder
- branching questions
- session-linked responses
- export shape version `component-d-v1`
- offline/queued sync state

## Current Build Note

Gradle wrapper is included. A CLI build was attempted, but this machine currently has no Android SDK configured, so Gradle stops at SDK discovery. Open Android Studio or install/configure the Android SDK, then run:

```bash
./gradlew :app:assembleDebug
```

The project itself is now native Kotlin/Compose and no longer depends on React Native, Expo, npm, or Metro.
