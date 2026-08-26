#include <Arduino.h>
#include <Wire.h>
#include <driver/i2s.h>
#include <BLE2902.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>

namespace Pins {
constexpr int MaxSda = 1;
constexpr int MaxScl = 2;
constexpr int MicSck = 6;
constexpr int MicWs = 7;
constexpr int MicSd = 8;
}

namespace BleContract {
constexpr const char* DeviceName = "WearableHealthMonitor";
constexpr const char* ServiceUuid = "9f2d7a10-9c1b-4f3d-8a6e-7b35e2a10000";
constexpr const char* TelemetryCharacteristicUuid = "9f2d7a11-9c1b-4f3d-8a6e-7b35e2a10000";
}

namespace Max30100 {
constexpr uint8_t Address = 0x57;
constexpr uint8_t RegInterruptStatus = 0x00;
constexpr uint8_t RegFifoWritePointer = 0x02;
constexpr uint8_t RegOverflowCounter = 0x03;
constexpr uint8_t RegFifoReadPointer = 0x04;
constexpr uint8_t RegFifoData = 0x05;
constexpr uint8_t RegModeConfig = 0x06;
constexpr uint8_t RegSpo2Config = 0x07;
constexpr uint8_t RegLedConfig = 0x09;
constexpr uint8_t RegPartId = 0xFF;
}

struct Telemetry {
  uint32_t timestampMs = 0;
  uint32_t ir = 0;
  uint32_t red = 0;
  uint32_t noiseAverage = 0;
  uint32_t noisePeak = 0;
  int statusFlags = 0;
};

BLEServer* bleServer = nullptr;
BLECharacteristic* telemetryCharacteristic = nullptr;
bool phoneConnected = false;
bool previousPhoneConnected = false;
Telemetry latestTelemetry;

constexpr uint32_t PpgSampleIntervalMs = 10;
constexpr uint32_t NoiseSampleIntervalMs = 40;
constexpr uint32_t BleNotifyIntervalMs = 200;
constexpr int I2sSampleRateHz = 16000;
constexpr int I2sReadSamples = 128;
constexpr i2s_port_t I2sPort = I2S_NUM_0;

uint32_t lastPpgSampleAt = 0;
uint32_t lastNoiseSampleAt = 0;
uint32_t lastBleNotifyAt = 0;
uint32_t lastSerialPrintAt = 0;

bool writeRegister(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(Max30100::Address);
  Wire.write(reg);
  Wire.write(value);
  return Wire.endTransmission() == 0;
}

bool readRegister(uint8_t reg, uint8_t& value) {
  Wire.beginTransmission(Max30100::Address);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(Max30100::Address, static_cast<uint8_t>(1)) != 1) return false;
  value = Wire.read();
  return true;
}

bool readBurst(uint8_t reg, uint8_t* buffer, size_t length) {
  Wire.beginTransmission(Max30100::Address);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(Max30100::Address, static_cast<uint8_t>(length)) != length) return false;
  for (size_t i = 0; i < length; i++) {
    buffer[i] = Wire.read();
  }
  return true;
}

bool initMax30100() {
  Wire.begin(Pins::MaxSda, Pins::MaxScl);
  Wire.setClock(400000);
  delay(50);

  uint8_t partId = 0;
  if (!readRegister(Max30100::RegPartId, partId)) {
    Serial.println("MAX30100: Part ID read failed");
    return false;
  }

  Serial.printf("MAX30100: Part ID 0x%02X\n", partId);
  if (partId != 0x11) {
    Serial.println("MAX30100: Unexpected Part ID; continuing because wiring may still be correct");
  }

  writeRegister(Max30100::RegModeConfig, 0x40);
  delay(100);
  writeRegister(Max30100::RegFifoWritePointer, 0x00);
  writeRegister(Max30100::RegOverflowCounter, 0x00);
  writeRegister(Max30100::RegFifoReadPointer, 0x00);
  writeRegister(Max30100::RegSpo2Config, 0x47);
  writeRegister(Max30100::RegLedConfig, 0x5F);
  writeRegister(Max30100::RegModeConfig, 0x03);

  uint8_t ignored = 0;
  readRegister(Max30100::RegInterruptStatus, ignored);
  Serial.println("MAX30100: initialized on SDA GPIO1 / SCL GPIO2");
  return true;
}

bool readMax30100Sample(uint32_t& ir, uint32_t& red) {
  uint8_t bytes[4] = {0};
  if (!readBurst(Max30100::RegFifoData, bytes, sizeof(bytes))) {
    return false;
  }

  ir = (static_cast<uint16_t>(bytes[0]) << 8) | bytes[1];
  red = (static_cast<uint16_t>(bytes[2]) << 8) | bytes[3];
  return true;
}

bool initInmp441() {
  i2s_config_t i2sConfig = {
    .mode = static_cast<i2s_mode_t>(I2S_MODE_MASTER | I2S_MODE_RX),
    .sample_rate = I2sSampleRateHz,
    .bits_per_sample = I2S_BITS_PER_SAMPLE_32BIT,
    .channel_format = I2S_CHANNEL_FMT_ONLY_LEFT,
    .communication_format = I2S_COMM_FORMAT_STAND_I2S,
    .intr_alloc_flags = ESP_INTR_FLAG_LEVEL1,
    .dma_buf_count = 4,
    .dma_buf_len = 256,
    .use_apll = false,
    .tx_desc_auto_clear = false,
    .fixed_mclk = 0
  };

  i2s_pin_config_t pinConfig = {
    .bck_io_num = Pins::MicSck,
    .ws_io_num = Pins::MicWs,
    .data_out_num = I2S_PIN_NO_CHANGE,
    .data_in_num = Pins::MicSd
  };

  esp_err_t result = i2s_driver_install(I2sPort, &i2sConfig, 0, nullptr);
  if (result != ESP_OK) {
    Serial.printf("INMP441: i2s_driver_install failed: %d\n", result);
    return false;
  }

  result = i2s_set_pin(I2sPort, &pinConfig);
  if (result != ESP_OK) {
    Serial.printf("INMP441: i2s_set_pin failed: %d\n", result);
    return false;
  }

  i2s_zero_dma_buffer(I2sPort);
  Serial.println("INMP441: initialized LEFT channel on SCK GPIO6 / WS GPIO7 / SD GPIO8");
  return true;
}

void readNoiseWindow(uint32_t& average, uint32_t& peak) {
  int32_t samples[I2sReadSamples];
  size_t bytesRead = 0;
  esp_err_t result = i2s_read(I2sPort, samples, sizeof(samples), &bytesRead, 2 / portTICK_PERIOD_MS);
  if (result != ESP_OK || bytesRead == 0) {
    return;
  }

  const int count = bytesRead / sizeof(int32_t);
  uint64_t sum = 0;
  uint32_t maxValue = 0;
  for (int i = 0; i < count; i++) {
    int32_t sample = samples[i] >> 8;
    uint32_t magnitude = abs(sample);
    sum += magnitude;
    if (magnitude > maxValue) maxValue = magnitude;
  }

  average = static_cast<uint32_t>(sum / count);
  peak = maxValue;
}

class ServerCallbacks : public BLEServerCallbacks {
  void onConnect(BLEServer* server) override {
    phoneConnected = true;
    Serial.println("BLE: phone connected");
  }

  void onDisconnect(BLEServer* server) override {
    phoneConnected = false;
    Serial.println("BLE: phone disconnected");
  }
};

void initBle() {
  BLEDevice::init(BleContract::DeviceName);
  BLEDevice::setMTU(185);
  bleServer = BLEDevice::createServer();
  bleServer->setCallbacks(new ServerCallbacks());

  BLEService* service = bleServer->createService(BleContract::ServiceUuid);
  telemetryCharacteristic = service->createCharacteristic(
    BleContract::TelemetryCharacteristicUuid,
    BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY
  );
  telemetryCharacteristic->addDescriptor(new BLE2902());
  service->start();

  BLEAdvertising* advertising = BLEDevice::getAdvertising();
  advertising->addServiceUUID(BleContract::ServiceUuid);
  advertising->setScanResponse(true);
  advertising->setMinPreferred(0x06);
  advertising->setMinPreferred(0x12);
  BLEDevice::startAdvertising();
  Serial.printf("BLE: advertising as %s\n", BleContract::DeviceName);
}

String buildTelemetryJson(const Telemetry& telemetry) {
  char buffer[176];
  snprintf(
    buffer,
    sizeof(buffer),
    "{\"t\":%lu,\"ir\":%lu,\"red\":%lu,\"hr\":null,\"rr\":null,\"spo2\":null,\"nAvg\":%lu,\"nPeak\":%lu,\"temp\":null,\"bat\":null,\"flags\":%d}",
    static_cast<unsigned long>(telemetry.timestampMs),
    static_cast<unsigned long>(telemetry.ir),
    static_cast<unsigned long>(telemetry.red),
    static_cast<unsigned long>(telemetry.noiseAverage),
    static_cast<unsigned long>(telemetry.noisePeak),
    telemetry.statusFlags
  );
  return String(buffer);
}

void notifyTelemetry() {
  if (!phoneConnected || telemetryCharacteristic == nullptr) return;

  String payload = buildTelemetryJson(latestTelemetry);
  telemetryCharacteristic->setValue(payload.c_str());
  telemetryCharacteristic->notify();
}

void setup() {
  Serial.begin(115200);
  delay(500);
  Serial.println();
  Serial.println("WearableHealthMonitor ESP32-S3 Mini starting");

  bool maxReady = initMax30100();
  bool micReady = initInmp441();
  latestTelemetry.statusFlags = (maxReady ? 0 : 1) | (micReady ? 0 : 2);

  initBle();
}

void loop() {
  uint32_t now = millis();

  if (now - lastPpgSampleAt >= PpgSampleIntervalMs) {
    lastPpgSampleAt = now;
    uint32_t ir = 0;
    uint32_t red = 0;
    if (readMax30100Sample(ir, red)) {
      latestTelemetry.timestampMs = now;
      latestTelemetry.ir = ir;
      latestTelemetry.red = red;
      latestTelemetry.statusFlags &= ~1;
    } else {
      latestTelemetry.statusFlags |= 1;
    }
  }

  if (now - lastNoiseSampleAt >= NoiseSampleIntervalMs) {
    lastNoiseSampleAt = now;
    uint32_t average = latestTelemetry.noiseAverage;
    uint32_t peak = latestTelemetry.noisePeak;
    readNoiseWindow(average, peak);
    latestTelemetry.noiseAverage = average;
    latestTelemetry.noisePeak = peak;
  }

  if (now - lastBleNotifyAt >= BleNotifyIntervalMs) {
    lastBleNotifyAt = now;
    notifyTelemetry();
  }

  if (!phoneConnected && previousPhoneConnected) {
    delay(100);
    BLEDevice::startAdvertising();
    Serial.println("BLE: advertising resumed");
  }
  previousPhoneConnected = phoneConnected;

  if (now - lastSerialPrintAt >= 1000) {
    lastSerialPrintAt = now;
    Serial.printf(
      "IR: %lu | RED: %lu | NOISE AVG: %lu | NOISE PEAK: %lu | BLE: %s | FLAGS: %d\n",
      static_cast<unsigned long>(latestTelemetry.ir),
      static_cast<unsigned long>(latestTelemetry.red),
      static_cast<unsigned long>(latestTelemetry.noiseAverage),
      static_cast<unsigned long>(latestTelemetry.noisePeak),
      phoneConnected ? "connected" : "advertising",
      latestTelemetry.statusFlags
    );
  }
}
