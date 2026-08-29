# MindSync VR Mobile

This repository contains the mobile control hub and ESP32-S3 firmware for the AI-Based Adaptive VR Meditation System.

## Active Mobile Application

The active application is the React Native CLI project in [`react-native-app/`](react-native-app/README.md). It includes the TypeScript product UI, Zustand orchestration, native BLE ingestion, Component D questionnaire flow, VR/Unity integration boundary, session controls, and Android/iOS native projects.

```bash
cd react-native-app
npm install
npm start
```

Run Android from another terminal:

```bash
cd react-native-app
npm run android
```

Verified standalone Android build:

```text
react-native-app/builds/MindSync-VR-react-native-release-arm64.apk
```

## Legacy Kotlin Application

The root Gradle project and `app/` directory contain the previous Kotlin/Jetpack Compose implementation. They are retained as a migration reference for the Component D audio recorder, Android TextToSpeech behavior, BLE diagnostics, and future Unity native bridge work. New screens and orchestration should be implemented in `react-native-app/`.

The legacy app can still be built independently:

```bash
./gradlew :app:assembleDebug
```

## ESP32-S3 Firmware

The current firmware is:

```text
firmware/esp32_s3_wearable_health_monitor/esp32_s3_wearable_health_monitor.ino
```

It targets the MAX30100 on SDA GPIO1/SCL GPIO2 and the INMP441 on SCK GPIO6/WS GPIO7/SD GPIO8. It advertises `WearableHealthMonitor` and sends JSON telemetry notifications at approximately 5 Hz.

- Service UUID: `7c69f001-7f70-4b0a-9c91-93d7f91b1001`
- Telemetry UUID: `7c69f002-7f70-4b0a-9c91-93d7f91b1001`

See the React Native README for the full protocol, installation procedure, architecture, and physical BLE test steps.
