#include <Wire.h>
#include <Adafruit_MLX90614.h>

#define SDA_PIN 1
#define SCL_PIN 2

#define MLX90614_ADDR 0x5A

Adafruit_MLX90614 mlx;

// -----------------------------------------------------
// Scan entire I2C bus
// -----------------------------------------------------

void scanI2C() {

  Serial.println();
  Serial.println("Scanning I2C bus...");

  int devices = 0;

  for (uint8_t address = 1; address < 127; address++) {

    Wire.beginTransmission(address);
    uint8_t error = Wire.endTransmission();

    if (error == 0) {

      Serial.print("Device found at 0x");

      if (address < 16) {
        Serial.print("0");
      }

      Serial.println(address, HEX);

      devices++;
    }
  }

  Serial.print("Total devices found: ");
  Serial.println(devices);
  Serial.println();
}


// -----------------------------------------------------
// Setup
// -----------------------------------------------------

void setup() {

  Serial.begin(115200);
  delay(2000);

  Serial.println();
  Serial.println("====================================");
  Serial.println("      MLX90614 FULL SENSOR TEST");
  Serial.println("====================================");

  Serial.println("SDA = GPIO1");
  Serial.println("SCL = GPIO2");

  // Start I2C
  Wire.begin(SDA_PIN, SCL_PIN);

  // Start slow for reliable testing
  Wire.setClock(50000);

  delay(500);

  // ---------------------------------------------------
  // Scan bus
  // ---------------------------------------------------

  scanI2C();


  // ---------------------------------------------------
  // Check specifically for MLX90614
  // ---------------------------------------------------

  Wire.beginTransmission(MLX90614_ADDR);
  uint8_t error = Wire.endTransmission();

  if (error != 0) {

    Serial.println("ERROR:");
    Serial.println("MLX90614 NOT detected at address 0x5A");

    Serial.print("I2C error code: ");
    Serial.println(error);

    Serial.println();
    Serial.println("Check:");
    Serial.println("VIN -> 3V3");
    Serial.println("GND -> GND");
    Serial.println("SDA -> GPIO1");
    Serial.println("SCL -> GPIO2");
    Serial.println("4.7k pull-up: SDA -> 3V3");
    Serial.println("4.7k pull-up: SCL -> 3V3");

    while (true) {
      delay(1000);
    }
  }


  Serial.println("MLX90614 detected at 0x5A");


  // ---------------------------------------------------
  // Initialize sensor library
  // ---------------------------------------------------

  if (!mlx.begin(MLX90614_ADDR, &Wire)) {

    Serial.println("ERROR:");
    Serial.println("MLX90614 detected on I2C,");
    Serial.println("but sensor initialization FAILED.");

    while (true) {
      delay(1000);
    }
  }


  Serial.println("MLX90614 initialized successfully!");
  Serial.println();

  Serial.println("Point the sensor toward your skin.");
  Serial.println("Object temperature should change.");
  Serial.println("------------------------------------");
}


// -----------------------------------------------------
// Main loop
// -----------------------------------------------------

void loop() {

  float ambientC = mlx.readAmbientTempC();
  float objectC  = mlx.readObjectTempC();

  float ambientF = mlx.readAmbientTempF();
  float objectF  = mlx.readObjectTempF();


  Serial.print("Ambient: ");
  Serial.print(ambientC, 2);
  Serial.print(" C");

  Serial.print(" | Object/Skin: ");
  Serial.print(objectC, 2);
  Serial.print(" C");

  Serial.print(" | Ambient: ");
  Serial.print(ambientF, 2);
  Serial.print(" F");

  Serial.print(" | Object: ");
  Serial.print(objectF, 2);
  Serial.println(" F");


  delay(500);
}