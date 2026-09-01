#include <Wire.h>
#include <ESP_I2S.h>
#include <NimBLEDevice.h>

// =====================================================
// HARDWARE
// =====================================================

#define SDA_PIN 1
#define SCL_PIN 2

#define I2S_SCK 6
#define I2S_WS  7
#define I2S_SD  8

#define MAX30100_ADDR       0x57
#define REG_FIFO_WR_PTR     0x02
#define REG_OVF_COUNTER     0x03
#define REG_FIFO_RD_PTR     0x04
#define REG_FIFO_DATA       0x05
#define REG_MODE_CONFIG     0x06
#define REG_SPO2_CONFIG     0x07
#define REG_LED_CONFIG      0x09
#define REG_PART_ID         0xFF

// =====================================================
// BLE CONTRACT
// =====================================================

#define DEVICE_NAME        "WearableHealthMonitor"
#define SERVICE_UUID       "7c69f001-7f70-4b0a-9c91-93d7f91b1001"
#define TELEMETRY_UUID     "7c69f002-7f70-4b0a-9c91-93d7f91b1001"
#define RAW_PPG_UUID       "7c69f003-7f70-4b0a-9c91-93d7f91b1001"

constexpr uint8_t RAW_PPG_SAMPLES_PER_PACKET = 5;
constexpr size_t RAW_PPG_PACKET_BYTES = 1 + RAW_PPG_SAMPLES_PER_PACKET * 8;
constexpr uint32_t PPG_SAMPLE_INTERVAL_MS = 10;
constexpr uint32_t TELEMETRY_INTERVAL_MS = 200;
constexpr uint32_t SERIAL_INTERVAL_MS = 1000;

// Status flag bits. Temperature is intentionally absent until a real sensor works.
constexpr uint8_t STATUS_PPG_READY = 1 << 0;
constexpr uint8_t STATUS_NOISE_READY = 1 << 1;

// =====================================================
// STATE
// =====================================================

I2SClass I2S;

NimBLEServer* bleServer = nullptr;
NimBLECharacteristic* telemetryCharacteristic = nullptr;
NimBLECharacteristic* rawPpgCharacteristic = nullptr;

volatile bool bleConnected = false;
bool ppgAvailable = false;
bool microphoneAvailable = false;

uint16_t latestIR = 0;
uint16_t latestRED = 0;
uint32_t latestNoiseAvg = 0;
uint32_t latestNoisePeak = 0;

uint32_t rawPpgTimestamps[RAW_PPG_SAMPLES_PER_PACKET] = {0};
uint32_t rawPpgValues[RAW_PPG_SAMPLES_PER_PACKET] = {0};
uint8_t rawPpgSampleCount = 0;
uint32_t rawPpgPacketsSent = 0;

uint32_t lastTelemetryAt = 0;
uint32_t lastSerialAt = 0;

// =====================================================
// BLE CALLBACKS
// =====================================================

class ServerCallbacks : public NimBLEServerCallbacks {
  void onConnect(NimBLEServer* server, NimBLEConnInfo& connection) override {
    bleConnected = true;
    Serial.print("BLE: phone connected, handle=");
    Serial.println(connection.getConnHandle());
  }

  void onDisconnect(
    NimBLEServer* server,
    NimBLEConnInfo& connection,
    int reason
  ) override {
    bleConnected = false;
    rawPpgSampleCount = 0;
    Serial.print("BLE: phone disconnected, reason=");
    Serial.println(reason);
    NimBLEDevice::startAdvertising();
    Serial.println("BLE: advertising resumed");
  }
};

// =====================================================
// MAX30100
// =====================================================

bool writeMAX(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(MAX30100_ADDR);
  Wire.write(reg);
  Wire.write(value);
  return Wire.endTransmission() == 0;
}

bool readMAX(uint8_t reg, uint8_t& value) {
  Wire.beginTransmission(MAX30100_ADDR);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) {
    return false;
  }

  if (Wire.requestFrom(MAX30100_ADDR, static_cast<uint8_t>(1)) != 1) {
    return false;
  }

  value = Wire.read();
  return true;
}

bool setupMAX30100() {
  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(100000);

  uint8_t partId = 0;
  if (!readMAX(REG_PART_ID, partId)) {
    Serial.println("MAX30100: unable to read Part ID");
    return false;
  }

  Serial.print("MAX30100: Part ID 0x");
  Serial.println(partId, HEX);
  if (partId != 0x11) {
    Serial.println("MAX30100: unexpected Part ID; expected 0x11");
    return false;
  }

  writeMAX(REG_MODE_CONFIG, 0x40);
  delay(100);
  writeMAX(REG_FIFO_WR_PTR, 0x00);
  writeMAX(REG_OVF_COUNTER, 0x00);
  writeMAX(REG_FIFO_RD_PTR, 0x00);

  // 100 samples/second, high-resolution SpO2 mode.
  writeMAX(REG_SPO2_CONFIG, 0x47);

  // Lower LED current prevents the battery rail from dipping during startup.
  writeMAX(REG_LED_CONFIG, 0x55);
  writeMAX(REG_MODE_CONFIG, 0x03);

  Serial.println("MAX30100: initialized at 100 Hz with LED current 0x55");
  return true;
}

// =====================================================
// INMP441
// =====================================================

bool setupMicrophone() {
  I2S.setPins(I2S_SCK, I2S_WS, -1, I2S_SD);
  const bool started = I2S.begin(
    I2S_MODE_STD,
    16000,
    I2S_DATA_BIT_WIDTH_32BIT,
    I2S_SLOT_MODE_STEREO
  );

  if (!started) {
    Serial.println("INMP441: failed to start");
    return false;
  }

  Serial.println("INMP441: initialized, LEFT channel on GPIO6/GPIO7/GPIO8");
  return true;
}

void readMicrophone() {
  int32_t stereoSamples[256];
  const size_t bytesRead = I2S.readBytes(
    reinterpret_cast<char*>(stereoSamples),
    sizeof(stereoSamples)
  );
  const size_t sampleCount = bytesRead / sizeof(int32_t);

  uint64_t absoluteSum = 0;
  uint32_t peak = 0;
  size_t leftCount = 0;

  // L/R is tied to GND, so valid microphone data is in the LEFT slot.
  for (size_t index = 0; index + 1 < sampleCount; index += 2) {
    const int32_t left = stereoSamples[index] >> 8;
    const uint32_t magnitude = left < 0
      ? static_cast<uint32_t>(-static_cast<int64_t>(left))
      : static_cast<uint32_t>(left);
    absoluteSum += magnitude;
    if (magnitude > peak) {
      peak = magnitude;
    }
    leftCount++;
  }

  latestNoiseAvg = leftCount > 0
    ? static_cast<uint32_t>(absoluteSum / leftCount)
    : 0;
  latestNoisePeak = peak;
}

// =====================================================
// BLE SETUP
// =====================================================

void setupBLE() {
  NimBLEDevice::init(DEVICE_NAME);
  NimBLEDevice::setMTU(185);

  bleServer = NimBLEDevice::createServer();
  bleServer->setCallbacks(new ServerCallbacks());

  NimBLEService* service = bleServer->createService(SERVICE_UUID);
  telemetryCharacteristic = service->createCharacteristic(
    TELEMETRY_UUID,
    NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::NOTIFY
  );
  rawPpgCharacteristic = service->createCharacteristic(
    RAW_PPG_UUID,
    NIMBLE_PROPERTY::NOTIFY
  );

  telemetryCharacteristic->setValue(
    "{\"t\":0,\"ir\":0,\"red\":0,\"noiseAvg\":0,\"noisePeak\":0,\"temp\":null,\"flags\":0}"
  );
  NimBLEAdvertising* advertising = NimBLEDevice::getAdvertising();
  advertising->addServiceUUID(SERVICE_UUID);
  advertising->setName(DEVICE_NAME);
  advertising->start();

  Serial.print("BLE: advertising as ");
  Serial.println(DEVICE_NAME);
  Serial.print("BLE: service ");
  Serial.println(SERVICE_UUID);
  Serial.print("BLE: telemetry ");
  Serial.println(TELEMETRY_UUID);
  Serial.print("BLE: raw PPG ");
  Serial.println(RAW_PPG_UUID);
}

// =====================================================
// RAW PPG BATCHING
// =====================================================

void writeLittleEndianU32(uint8_t* output, uint32_t value) {
  output[0] = static_cast<uint8_t>(value);
  output[1] = static_cast<uint8_t>(value >> 8);
  output[2] = static_cast<uint8_t>(value >> 16);
  output[3] = static_cast<uint8_t>(value >> 24);
}

void notifyRawPpgBatch() {
  if (!bleConnected || rawPpgSampleCount == 0) {
    rawPpgSampleCount = 0;
    return;
  }

  uint8_t packet[RAW_PPG_PACKET_BYTES] = {0};
  packet[0] = rawPpgSampleCount;
  for (uint8_t index = 0; index < rawPpgSampleCount; index++) {
    const size_t offset = 1 + index * 8;
    writeLittleEndianU32(packet + offset, rawPpgTimestamps[index]);
    writeLittleEndianU32(packet + offset + 4, rawPpgValues[index]);
  }

  rawPpgCharacteristic->setValue(packet, sizeof(packet));
  rawPpgCharacteristic->notify();
  rawPpgPacketsSent++;
  rawPpgSampleCount = 0;
}

void queueRawPpgSample(uint32_t timestampMs, uint32_t irValue) {
  if (!bleConnected) {
    rawPpgSampleCount = 0;
    return;
  }

  rawPpgTimestamps[rawPpgSampleCount] = timestampMs;
  rawPpgValues[rawPpgSampleCount] = irValue;
  rawPpgSampleCount++;

  if (rawPpgSampleCount == RAW_PPG_SAMPLES_PER_PACKET) {
    notifyRawPpgBatch();
  }
}

void readMAX30100() {
  uint8_t writePointer = 0;
  uint8_t readPointer = 0;
  if (!readMAX(REG_FIFO_WR_PTR, writePointer) ||
      !readMAX(REG_FIFO_RD_PTR, readPointer)) {
    return;
  }

  int samplesAvailable = (writePointer - readPointer) & 0x0F;
  const uint32_t newestTimestamp = millis();

  while (samplesAvailable > 0) {
    Wire.beginTransmission(MAX30100_ADDR);
    Wire.write(REG_FIFO_DATA);
    if (Wire.endTransmission(false) != 0) {
      break;
    }

    if (Wire.requestFrom(MAX30100_ADDR, static_cast<uint8_t>(4)) != 4) {
      break;
    }

    latestIR =
      (static_cast<uint16_t>(Wire.read()) << 8) |
      static_cast<uint16_t>(Wire.read());
    latestRED =
      (static_cast<uint16_t>(Wire.read()) << 8) |
      static_cast<uint16_t>(Wire.read());

    const uint32_t sampleTimestamp = newestTimestamp -
      static_cast<uint32_t>(samplesAvailable - 1) * PPG_SAMPLE_INTERVAL_MS;
    queueRawPpgSample(sampleTimestamp, latestIR);
    samplesAvailable--;
  }
}

// =====================================================
// TELEMETRY
// =====================================================

void sendTelemetry() {
  if (!bleConnected) {
    return;
  }

  const uint8_t flags =
    (latestIR > 0 ? STATUS_PPG_READY : 0) |
    (latestNoisePeak > 0 ? STATUS_NOISE_READY : 0);

  char payload[192];
  snprintf(
    payload,
    sizeof(payload),
    "{\"t\":%lu,\"ir\":%u,\"red\":%u,\"noiseAvg\":%lu,\"noisePeak\":%lu,\"temp\":null,\"flags\":%u}",
    static_cast<unsigned long>(millis()),
    latestIR,
    latestRED,
    static_cast<unsigned long>(latestNoiseAvg),
    static_cast<unsigned long>(latestNoisePeak),
    flags
  );

  telemetryCharacteristic->setValue(payload);
  telemetryCharacteristic->notify();
}

// =====================================================
// ARDUINO ENTRY POINTS
// =====================================================

void setup() {
  Serial.begin(115200);
  delay(2000);

  Serial.println();
  Serial.println("============================================");
  Serial.println("MindSync Wearable: PPG + noise + BLE relay");
  Serial.println("Temperature: unavailable on device (null)");
  Serial.println("============================================");

  ppgAvailable = setupMAX30100();
  microphoneAvailable = setupMicrophone();
  setupBLE();

  if (!ppgAvailable) {
    Serial.println("WARNING: PPG acquisition is unavailable");
  }
  if (!microphoneAvailable) {
    Serial.println("WARNING: noise acquisition is unavailable");
  }
  Serial.println("System ready");
}

void loop() {
  if (ppgAvailable) {
    readMAX30100();
  }
  if (microphoneAvailable) {
    readMicrophone();
  }

  const uint32_t now = millis();
  if (now - lastTelemetryAt >= TELEMETRY_INTERVAL_MS) {
    lastTelemetryAt = now;
    sendTelemetry();
  }

  if (now - lastSerialAt >= SERIAL_INTERVAL_MS) {
    lastSerialAt = now;
    Serial.printf(
      "IR: %u | RED: %u | NOISE AVG: %lu | NOISE PEAK: %lu | RAW PACKETS: %lu | BLE: %s | TEMP: \n",
      latestIR,
      latestRED,
      static_cast<unsigned long>(latestNoiseAvg),
      static_cast<unsigned long>(latestNoisePeak),
      static_cast<unsigned long>(rawPpgPacketsSent),
      bleConnected ? "connected" : "advertising"
    );
  }

  delay(1);
}
