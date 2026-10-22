const UINT32_RANGE = 0x1_0000_0000;
const WRAP_HIGH_WATERMARK = 0xf000_0000;
const WRAP_LOW_WATERMARK = 0x0fff_ffff;

export class DeviceUptimeFreshnessGuard {
  private clockOffsetMs: number | null = null;
  private lastTimestampMs: number | null = null;
  private wrapOffsetMs = 0;

  constructor(private readonly maxDeliveryLagMs: number) {}

  reset() {
    this.clockOffsetMs = null;
    this.lastTimestampMs = null;
    this.wrapOffsetMs = 0;
  }

  isFresh(timestampMs: number | null, receivedAtMs: number) {
    if (timestampMs === null) return true;

    if (this.lastTimestampMs !== null && timestampMs < this.lastTimestampMs) {
      const wrapped = this.lastTimestampMs > WRAP_HIGH_WATERMARK && timestampMs < WRAP_LOW_WATERMARK;
      if (wrapped) {
        this.wrapOffsetMs += UINT32_RANGE;
      } else {
        this.reset();
      }
    }

    this.lastTimestampMs = timestampMs;
    const unwrappedTimestampMs = timestampMs + this.wrapOffsetMs;
    if (this.clockOffsetMs === null) {
      this.clockOffsetMs = receivedAtMs - unwrappedTimestampMs;
      return true;
    }

    const expectedReceiptMs = unwrappedTimestampMs + this.clockOffsetMs;
    return receivedAtMs - expectedReceiptMs <= this.maxDeliveryLagMs;
  }
}
