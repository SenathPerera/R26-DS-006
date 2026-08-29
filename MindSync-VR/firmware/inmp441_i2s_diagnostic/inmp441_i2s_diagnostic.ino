#include <Arduino.h>
#include <driver/i2s.h>

constexpr int MicSck = 6;
constexpr int MicWs = 7;
constexpr int MicSd = 8;

constexpr i2s_port_t I2sPort = I2S_NUM_0;
constexpr int SampleRateHz = 16000;
constexpr int ReadSamples = 256;

void setupI2s() {
  i2s_config_t config = {
    .mode = static_cast<i2s_mode_t>(I2S_MODE_MASTER | I2S_MODE_RX),
    .sample_rate = SampleRateHz,
    .bits_per_sample = I2S_BITS_PER_SAMPLE_32BIT,
    .channel_format = I2S_CHANNEL_FMT_RIGHT_LEFT,
    .communication_format = I2S_COMM_FORMAT_STAND_I2S,
    .intr_alloc_flags = ESP_INTR_FLAG_LEVEL1,
    .dma_buf_count = 4,
    .dma_buf_len = 256,
    .use_apll = false,
    .tx_desc_auto_clear = false,
    .fixed_mclk = 0
  };

  i2s_pin_config_t pins = {
    .bck_io_num = MicSck,
    .ws_io_num = MicWs,
    .data_out_num = I2S_PIN_NO_CHANGE,
    .data_in_num = MicSd
  };

  esp_err_t result = i2s_driver_install(I2sPort, &config, 0, nullptr);
  Serial.printf("i2s_driver_install: %d\n", result);

  result = i2s_set_pin(I2sPort, &pins);
  Serial.printf("i2s_set_pin: %d\n", result);

  i2s_zero_dma_buffer(I2sPort);
}

void setup() {
  Serial.begin(115200);
  delay(500);
  Serial.println();
  Serial.println("INMP441 I2S diagnostic");
  Serial.println("Wiring expected: SCK GPIO6, WS GPIO7, SD GPIO8, L/R GND, VDD 3V3");
  setupI2s();
}

void loop() {
  int32_t samples[ReadSamples];
  size_t bytesRead = 0;
  esp_err_t result = i2s_read(
    I2sPort,
    samples,
    sizeof(samples),
    &bytesRead,
    100 / portTICK_PERIOD_MS
  );

  uint32_t leftPeakRaw = 0;
  uint32_t rightPeakRaw = 0;
  uint32_t leftPeakShift8 = 0;
  uint32_t rightPeakShift8 = 0;
  uint32_t nonZeroRaw = 0;
  int32_t firstSamples[8] = {0};

  const int count = bytesRead / sizeof(int32_t);
  for (int i = 0; i < count; i++) {
    int32_t raw = samples[i];
    uint32_t rawMag = abs(raw);
    uint32_t shift8Mag = abs(raw >> 8);
    if (raw != 0) nonZeroRaw++;
    if (i < 8) firstSamples[i] = raw;

    if ((i % 2) == 0) {
      if (rawMag > leftPeakRaw) leftPeakRaw = rawMag;
      if (shift8Mag > leftPeakShift8) leftPeakShift8 = shift8Mag;
    } else {
      if (rawMag > rightPeakRaw) rightPeakRaw = rawMag;
      if (shift8Mag > rightPeakShift8) rightPeakShift8 = shift8Mag;
    }
  }

  Serial.printf(
    "result=%d bytes=%u count=%d nonZero=%lu leftRaw=%lu rightRaw=%lu leftShift8=%lu rightShift8=%lu first=[%ld,%ld,%ld,%ld,%ld,%ld,%ld,%ld]\n",
    result,
    static_cast<unsigned int>(bytesRead),
    count,
    static_cast<unsigned long>(nonZeroRaw),
    static_cast<unsigned long>(leftPeakRaw),
    static_cast<unsigned long>(rightPeakRaw),
    static_cast<unsigned long>(leftPeakShift8),
    static_cast<unsigned long>(rightPeakShift8),
    static_cast<long>(firstSamples[0]),
    static_cast<long>(firstSamples[1]),
    static_cast<long>(firstSamples[2]),
    static_cast<long>(firstSamples[3]),
    static_cast<long>(firstSamples[4]),
    static_cast<long>(firstSamples[5]),
    static_cast<long>(firstSamples[6]),
    static_cast<long>(firstSamples[7])
  );

  delay(500);
}
