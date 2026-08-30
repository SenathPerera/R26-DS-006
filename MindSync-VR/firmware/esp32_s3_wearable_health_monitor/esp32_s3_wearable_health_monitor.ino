#include <Wire.h>
#include <ESP_I2S.h>
#include <NimBLEDevice.h>

// =====================================================
// PINS
// =====================================================

// MAX30100
#define SDA_PIN 1
#define SCL_PIN 2

// INMP441
#define I2S_SCK 6
#define I2S_WS  7
#define I2S_SD  8

// =====================================================
// MAX30100 REGISTERS
// =====================================================

#define MAX30100_ADDR       0x57
#define REG_FIFO_WR_PTR     0x02
#define REG_OVF_COUNTER     0x03
#define REG_FIFO_RD_PTR     0x04
#define REG_FIFO_DATA       0x05
#define REG_MODE_CONFIG     0x06
#define REG_SPO2_CONFIG     0x07
#define REG_LED_CONFIG      0x09
#define REG_PART_ID         0xFF

// TMP117 shares the MAX30100 I2C bus.
#define TMP117_ADDR         0x48
#define TMP117_TEMP_RESULT  0x00
#define TMP117_DEVICE_ID    0x0F
#define TMP117_EXPECTED_ID  0x0117

// =====================================================
// BLE UUIDs
// =====================================================

#define SERVICE_UUID        "7c69f001-7f70-4b0a-9c91-93d7f91b1001"
#define TELEMETRY_UUID      "7c69f002-7f70-4b0a-9c91-93d7f91b1001"
#define RAW_PPG_UUID        "7c69f003-7f70-4b0a-9c91-93d7f91b1001"

#define RAW_PPG_SAMPLES_PER_PACKET 5
#define RAW_PPG_PACKET_BYTES (1 + RAW_PPG_SAMPLES_PER_PACKET * 8)
#define PPG_SAMPLE_INTERVAL_MS 10

// =====================================================
// GLOBALS
// =====================================================

I2SClass I2S;

NimBLEServer* bleServer = nullptr;
NimBLECharacteristic* telemetryCharacteristic = nullptr;
NimBLECharacteristic* rawPpgCharacteristic = nullptr;

volatile bool bleConnected = false;

uint16_t latestIR = 0;
uint16_t latestRED = 0;

long latestNoiseAvg = 0;
long latestNoisePeak = 0;
float latestTemperatureC = NAN;
bool temperatureAvailable = false;

uint32_t rawPpgTimestamps[RAW_PPG_SAMPLES_PER_PACKET] = {0};
uint32_t rawPpgValues[RAW_PPG_SAMPLES_PER_PACKET] = {0};
uint8_t rawPpgSampleCount = 0;
uint32_t rawPpgPacketsSent = 0;

unsigned long lastSerialPrint = 0;
unsigned long lastBleSend = 0;
unsigned long lastTemperatureRead = 0;

// =====================================================
// BLE CALLBACKS
// =====================================================

class ServerCallbacks : public NimBLEServerCallbacks {
  void onConnect(NimBLEServer* pServer, NimBLEConnInfo& connInfo) override {
    bleConnected = true;
    Serial.println("BLE connected");
  }

  void onDisconnect(NimBLEServer* pServer, NimBLEConnInfo& connInfo, int reason) override {
    bleConnected = false;
    Serial.println("BLE disconnected");
    NimBLEDevice::startAdvertising();
  }
};

// =====================================================
// MAX30100 HELPERS
// =====================================================

void writeMAX(byte reg, byte value) {
  Wire.beginTransmission(MAX30100_ADDR);
  Wire.write(reg);
  Wire.write(value);
  Wire.endTransmission();
}

byte readMAX(byte reg) {
  Wire.beginTransmission(MAX30100_ADDR);
  Wire.write(reg);

  if (Wire.endTransmission(false) != 0) {
    return 0;
  }

  Wire.requestFrom(MAX30100_ADDR, (uint8_t)1);

  if (Wire.available()) {
    return Wire.read();
  }

  return 0;
}

bool readI2CRegister16(uint8_t address, uint8_t reg, uint16_t& value) {
  Wire.beginTransmission(address);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) {
    return false;
  }

  if (Wire.requestFrom(address, (uint8_t)2) != 2 || Wire.available() < 2) {
    return false;
  }

  value = ((uint16_t)Wire.read() << 8) | Wire.read();
  return true;
}

// =====================================================
// SETUP MAX30100
// =====================================================

void setupMAX30100() {
  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(100000);

  byte partID = readMAX(REG_PART_ID);

  Serial.print("MAX30100 Part ID: 0x");
  Serial.println(partID, HEX);

  if (partID == 0x11) {
    Serial.println("MAX30100 detected OK");
  } else {
    Serial.println("WARNING: MAX30100 not detected correctly");
  }

  // Reset
  writeMAX(REG_MODE_CONFIG, 0x40);
  delay(100);

  // Clear FIFO
  writeMAX(REG_FIFO_WR_PTR, 0x00);
  writeMAX(REG_OVF_COUNTER, 0x00);
  writeMAX(REG_FIFO_RD_PTR, 0x00);

  // 100 samples/sec, high resolution
  writeMAX(REG_SPO2_CONFIG, 0x47);

  // IMPORTANT:
  // Reduced LED current for battery stability test.
  writeMAX(REG_LED_CONFIG, 0x55);

  // SpO2 mode = RED + IR
  writeMAX(REG_MODE_CONFIG, 0x03);

  Serial.println("MAX30100 configured with LED current 0x55");
}

// =====================================================
// SETUP / READ TMP117
// =====================================================

void setupTemperatureSensor() {
  uint16_t deviceId = 0;
  temperatureAvailable = readI2CRegister16(
    TMP117_ADDR,
    TMP117_DEVICE_ID,
    deviceId
  );

  if (!temperatureAvailable) {
    Serial.println("TMP117: not detected; temperature will be null");
    return;
  }

  Serial.print("TMP117: device ID 0x");
  Serial.println(deviceId, HEX);
  if (deviceId != TMP117_EXPECTED_ID) {
    Serial.println("TMP117: unexpected device ID; verify the sensor module");
    temperatureAvailable = false;
  } else {
    Serial.println("TMP117: initialized on shared SDA GPIO1 / SCL GPIO2");
  }
}

void readTemperatureSensor() {
  if (!temperatureAvailable || millis() - lastTemperatureRead < 250) {
    return;
  }
  lastTemperatureRead = millis();

  uint16_t rawValue = 0;
  if (!readI2CRegister16(TMP117_ADDR, TMP117_TEMP_RESULT, rawValue)) {
    latestTemperatureC = NAN;
    return;
  }

  latestTemperatureC = (int16_t)rawValue * 0.0078125f;
}

// =====================================================
// SETUP INMP441
// =====================================================

void setupMicrophone() {
  I2S.setPins(
    I2S_SCK,
    I2S_WS,
    -1,
    I2S_SD
  );

  bool micOK = I2S.begin(
    I2S_MODE_STD,
    16000,
    I2S_DATA_BIT_WIDTH_32BIT,
    I2S_SLOT_MODE_STEREO
  );

  if (!micOK) {
    Serial.println("INMP441 failed to start");
    while (1) {
      delay(1000);
    }
  }

  Serial.println("INMP441 started OK");
}

// =====================================================
// SETUP BLE
// =====================================================

void setupBLE() {
  NimBLEDevice::init("WearableHealthMonitor");
  NimBLEDevice::setMTU(185);

  bleServer = NimBLEDevice::createServer();
  bleServer->setCallbacks(new ServerCallbacks());

  NimBLEService* service =
    bleServer->createService(SERVICE_UUID);

  telemetryCharacteristic =
    service->createCharacteristic(
      TELEMETRY_UUID,
      NIMBLE_PROPERTY::READ |
      NIMBLE_PROPERTY::NOTIFY
    );

  telemetryCharacteristic->setValue(
    "{\"status\":\"ready\"}"
  );

  rawPpgCharacteristic =
    service->createCharacteristic(
      RAW_PPG_UUID,
      NIMBLE_PROPERTY::NOTIFY
    );

  service->start();

  NimBLEAdvertising* advertising =
    NimBLEDevice::getAdvertising();

  advertising->addServiceUUID(SERVICE_UUID);
  advertising->setName("WearableHealthMonitor");

  advertising->start();

  Serial.println("BLE advertising started");
  Serial.print("BLE raw PPG characteristic: ");
  Serial.println(RAW_PPG_UUID);
}

// =====================================================
// RAW PPG BLE BATCHING
// =====================================================

void writeLittleEndianU32(uint8_t* output, uint32_t value) {
  output[0] = value & 0xFF;
  output[1] = (value >> 8) & 0xFF;
  output[2] = (value >> 16) & 0xFF;
  output[3] = (value >> 24) & 0xFF;
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

// =====================================================
// READ MAX30100
// =====================================================

void readMAX30100() {
  byte writePointer = readMAX(REG_FIFO_WR_PTR);
  byte readPointer  = readMAX(REG_FIFO_RD_PTR);

  int samplesAvailable =
    (writePointer - readPointer) & 0x0F;

  const uint32_t newestSampleTimestamp = millis();

  while (samplesAvailable > 0) {
    Wire.beginTransmission(MAX30100_ADDR);
    Wire.write(REG_FIFO_DATA);
    Wire.endTransmission(false);

    Wire.requestFrom(
      MAX30100_ADDR,
      (uint8_t)4
    );

    if (Wire.available() >= 4) {
      latestIR =
        ((uint16_t)Wire.read() << 8) |
        Wire.read();

      latestRED =
        ((uint16_t)Wire.read() << 8) |
        Wire.read();

      const uint32_t sampleTimestamp = newestSampleTimestamp -
        (uint32_t)(samplesAvailable - 1) * PPG_SAMPLE_INTERVAL_MS;
      queueRawPpgSample(sampleTimestamp, latestIR);
    }

    samplesAvailable--;
  }
}

// =====================================================
// READ INMP441
// =====================================================

void readMicrophone() {
  int32_t audioSamples[256];

  size_t bytesRead =
    I2S.readBytes(
      (char*)audioSamples,
      sizeof(audioSamples)
    );

  int sampleCount =
    bytesRead / sizeof(int32_t);

  int64_t totalLeft = 0;
  int32_t peak = 0;
  int leftCount = 0;

  for (int i = 0; i + 1 < sampleCount; i += 2) {
    int32_t leftSample =
      audioSamples[i] >> 8;

    int64_t absValue = leftSample;

    if (absValue < 0) {
      absValue = -absValue;
    }

    totalLeft += absValue;
    leftCount++;

    if (absValue > peak) {
      peak = absValue;
    }
  }

  latestNoiseAvg =
    leftCount > 0
      ? totalLeft / leftCount
      : 0;

  latestNoisePeak = peak;
}

// =====================================================
// SEND BLE TELEMETRY
// =====================================================

void sendBLETelemetry() {
  if (!bleConnected) {
    return;
  }

  char payload[180];
  char temperature[16];
  if (temperatureAvailable && isfinite(latestTemperatureC)) {
    snprintf(temperature, sizeof(temperature), "%.3f", latestTemperatureC);
  } else {
    snprintf(temperature, sizeof(temperature), "null");
  }

  const uint8_t statusFlags =
    (latestIR > 0 ? 1 : 0) |
    (latestNoisePeak > 0 ? 2 : 0) |
    (temperatureAvailable && isfinite(latestTemperatureC) ? 4 : 0);

  snprintf(
    payload,
    sizeof(payload),
    "{\"t\":%lu,\"ir\":%u,\"red\":%u,\"noiseAvg\":%ld,\"noisePeak\":%ld,\"temp\":%s,\"flags\":%u}",
    millis(),
    latestIR,
    latestRED,
    latestNoiseAvg,
    latestNoisePeak,
    temperature,
    statusFlags
  );

  telemetryCharacteristic->setValue(payload);
  telemetryCharacteristic->notify();
}

// =====================================================
// SETUP
// =====================================================

void setup() {
  Serial.begin(115200);
  delay(2000);

  Serial.println();
  Serial.println("======================================");
  Serial.println("BATTERY STABILITY + BLE SENSOR TEST");
  Serial.println("======================================");

  setupMAX30100();
  setupTemperatureSensor();
  setupMicrophone();
  setupBLE();

  Serial.println();
  Serial.println("System ready");
  Serial.println("MAX30100 LED current = 0x55");
  Serial.println();
}

// =====================================================
// LOOP
// =====================================================

void loop() {
  readMAX30100();
  readTemperatureSensor();
  readMicrophone();

  // BLE telemetry every 200 ms
  if (millis() - lastBleSend >= 200) {
    lastBleSend = millis();
    sendBLETelemetry();
  }

  // Serial debug every 500 ms
  if (millis() - lastSerialPrint >= 500) {
    lastSerialPrint = millis();

    Serial.print("IR: ");
    Serial.print(latestIR);

    Serial.print(" | RED: ");
    Serial.print(latestRED);

    Serial.print(" | NOISE AVG: ");
    Serial.print(latestNoiseAvg);

    Serial.print(" | NOISE PEAK: ");
    Serial.print(latestNoisePeak);

    Serial.print(" | TEMP C: ");
    if (temperatureAvailable && isfinite(latestTemperatureC)) {
      Serial.print(latestTemperatureC, 3);
    } else {
      Serial.print("N/A");
    }

    Serial.print(" | RAW PACKETS: ");
    Serial.print(rawPpgPacketsSent);

    Serial.print(" | BLE: ");
    Serial.println(
      bleConnected ? "CONNECTED" : "WAITING"
    );
  }

  delay(5);
}
