import {ComponentBFrame, RawPpgBatch} from '../../types/domain';

export const COMPONENT_B_SAMPLE_RATE_HZ = 64.0 as const;
export const COMPONENT_B_FRAME_SAMPLES = 960;
export const COMPONENT_B_FRAME_SECONDS = COMPONENT_B_FRAME_SAMPLES / COMPONENT_B_SAMPLE_RATE_HZ;

const TARGET_SAMPLE_INTERVAL_MS = 1000 / COMPONENT_B_SAMPLE_RATE_HZ;
const MAX_SOURCE_GAP_MS = 35;
const UINT32_RANGE = 0x1_0000_0000;

type TimedAmplitude = {timestampMs: number; amplitude: number};

type Callbacks = {
  onFrame: (frame: ComponentBFrame) => void;
  onProgress?: (sampleCount: number) => void;
  onDiscontinuity?: (message: string) => void;
};

export class ComponentBFrameAssembler {
  private previous: TimedAmplitude | null = null;
  private nextTargetTimestampMs: number | null = null;
  private frameStartTimestampMs: number | null = null;
  private frame: number[] = [];
  private latestTemperatureC: number | null = null;
  private phoneClockOffsetMs: number | null = null;
  private lastRawTimestampMs: number | null = null;
  private timestampWrapOffsetMs = 0;

  constructor(private readonly callbacks: Callbacks) {}

  setTemperature(temperatureC: number | null) {
    this.latestTemperatureC = temperatureC !== null && Number.isFinite(temperatureC)
      ? temperatureC
      : null;
  }

  ingest(batch: RawPpgBatch) {
    if (batch.samples.length === 0) return;

    const samples = batch.samples.map(sample => ({
      timestampMs: this.unwrapTimestamp(sample.timestampMs),
      amplitude: sample.irValue,
    }));

    if (this.phoneClockOffsetMs === null) {
      this.phoneClockOffsetMs = batch.receivedAtMs - samples[samples.length - 1].timestampMs;
    }

    for (const sample of samples) this.ingestSample(sample);
    this.callbacks.onProgress?.(this.frame.length);
  }

  reset() {
    this.resetFrame();
    this.latestTemperatureC = null;
    this.phoneClockOffsetMs = null;
    this.lastRawTimestampMs = null;
    this.timestampWrapOffsetMs = 0;
  }

  private unwrapTimestamp(rawTimestampMs: number) {
    if (this.lastRawTimestampMs !== null && rawTimestampMs < this.lastRawTimestampMs) {
      const isWrap = this.lastRawTimestampMs > 0xf0000000 && rawTimestampMs < 0x0fffffff;
      if (isWrap) {
        this.timestampWrapOffsetMs += UINT32_RANGE;
      } else {
        this.callbacks.onDiscontinuity?.('Wearable uptime restarted; partial Component B frame was discarded');
        this.resetFrame();
        this.phoneClockOffsetMs = null;
        this.timestampWrapOffsetMs = 0;
      }
    }
    this.lastRawTimestampMs = rawTimestampMs;
    return rawTimestampMs + this.timestampWrapOffsetMs;
  }

  private ingestSample(current: TimedAmplitude) {
    if (!Number.isFinite(current.amplitude)) return;

    if (!this.previous) {
      this.previous = current;
      this.nextTargetTimestampMs = current.timestampMs;
      this.frameStartTimestampMs = current.timestampMs;
      return;
    }

    const gapMs = current.timestampMs - this.previous.timestampMs;
    if (gapMs <= 0) return;
    if (gapMs > MAX_SOURCE_GAP_MS) {
      this.callbacks.onDiscontinuity?.(`Raw PPG gap ${gapMs.toFixed(0)} ms; partial Component B frame was discarded`);
      this.resetFrame();
      this.previous = current;
      this.nextTargetTimestampMs = current.timestampMs;
      this.frameStartTimestampMs = current.timestampMs;
      return;
    }

    while (this.nextTargetTimestampMs !== null && this.nextTargetTimestampMs <= current.timestampMs) {
      const ratio = (this.nextTargetTimestampMs - this.previous.timestampMs) / gapMs;
      const amplitude = this.previous.amplitude + ratio * (current.amplitude - this.previous.amplitude);
      this.frame.push(amplitude);
      this.nextTargetTimestampMs += TARGET_SAMPLE_INTERVAL_MS;

      if (this.frame.length === COMPONENT_B_FRAME_SAMPLES) this.emitFrame();
    }

    this.previous = current;
  }

  private emitFrame() {
    if (this.frameStartTimestampMs === null || this.phoneClockOffsetMs === null) return;
    this.callbacks.onFrame({
      timestamp: (this.frameStartTimestampMs + this.phoneClockOffsetMs) / 1000,
      sample_rate: COMPONENT_B_SAMPLE_RATE_HZ,
      ppg: this.frame,
      temperature: this.latestTemperatureC,
    });
    this.frame = [];
    this.frameStartTimestampMs = this.nextTargetTimestampMs;
    this.callbacks.onProgress?.(0);
  }

  private resetFrame() {
    this.previous = null;
    this.nextTargetTimestampMs = null;
    this.frameStartTimestampMs = null;
    this.frame = [];
    this.callbacks.onProgress?.(0);
  }
}
