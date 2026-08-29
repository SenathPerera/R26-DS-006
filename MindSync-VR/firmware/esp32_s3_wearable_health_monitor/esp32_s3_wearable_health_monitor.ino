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

// =====================================================
// BLE UUIDs
// =====================================================

#define SERVICE_UUID        "7c69f001-7f70-4b0a-9c91-93d7f91b1001"
#define TELEMETRY_UUID      "7c69f002-7f70-4b0a-9c91-93d7f91b1001"

// =====================================================
// GLOBALS
// =====================================================

I2SClass I2S;

NimBLEServer* bleServer = nullptr;
NimBLECharacteristic* telemetryCharacteristic = nullptr;

bool bleConnected = false;

uint16_t latestIR = 0;
uint16_t latestRED = 0;

long latestNoiseAvg = 0;
long latestNoisePeak = 0;

unsigned long lastSerialPrint = 0;
unsigned long lastBleSend = 0;

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

  service->start();

  NimBLEAdvertising* advertising =
    NimBLEDevice::getAdvertising();

  advertising->addServiceUUID(SERVICE_UUID);
  advertising->setName("WearableHealthMonitor");

  advertising->start();

  Serial.println("BLE advertising started");
}

// =====================================================
// READ MAX30100
// =====================================================

void readMAX30100() {
  byte writePointer = readMAX(REG_FIFO_WR_PTR);
  byte readPointer  = readMAX(REG_FIFO_RD_PTR);

  int samplesAvailable =
    (writePointer - readPointer) & 0x0F;

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

  char payload[160];

  snprintf(
    payload,
    sizeof(payload),
    "{\"ir\":%u,\"red\":%u,\"noiseAvg\":%ld,\"noisePeak\":%ld}",
    latestIR,
    latestRED,
    latestNoiseAvg,
    latestNoisePeak
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

    Serial.print(" | BLE: ");
    Serial.println(
      bleConnected ? "CONNECTED" : "WAITING"
    );
  }

  delay(5);
}