#include <NimBLEDevice.h>

constexpr char DIAGNOSTIC_DEVICE_NAME[] = "S3-BLE-TEST";
constexpr uint32_t SERIAL_BAUD = 115200;
constexpr uint32_t STATUS_INTERVAL_MS = 1000;

NimBLEAdvertising* advertising = nullptr;
uint32_t lastStatusAt = 0;

void setup() {
  Serial.begin(SERIAL_BAUD);
  delay(2000);

  Serial.println();
  Serial.println("ESP32-S3 BLE advertising diagnostic");

  NimBLEDevice::init(DIAGNOSTIC_DEVICE_NAME);
  const bool powerConfigured = NimBLEDevice::setPower(9);

  advertising = NimBLEDevice::getAdvertising();
  advertising->setMinInterval(160);  // 100 ms (units are 0.625 ms).
  advertising->setMaxInterval(240);  // 150 ms.

  const bool nameAdded = advertising->setName(DIAGNOSTIC_DEVICE_NAME);
  const bool advertisingStarted = advertising->start(0);

  Serial.print("BLE address: ");
  Serial.println(NimBLEDevice::getAddress().toString().c_str());
  Serial.printf("TX power: %s\n", powerConfigured ? "ok" : "failed");
  Serial.printf("Advertisement name: %s\n", nameAdded ? "ok" : "failed");
  Serial.printf("Advertisement start: %s\n", advertisingStarted ? "ok" : "failed");
  Serial.printf("Advertising active: %s\n", advertising->isAdvertising() ? "yes" : "no");
}

void loop() {
  const uint32_t now = millis();
  if (now - lastStatusAt >= STATUS_INTERVAL_MS) {
    lastStatusAt = now;
    Serial.printf(
      "Advertising active: %s | scan name: %s\n",
      advertising != nullptr && advertising->isAdvertising() ? "yes" : "no",
      DIAGNOSTIC_DEVICE_NAME
    );
  }

  delay(10);
}
