import {RawPpgBatch} from '../../types/domain';
import {
  COMPONENT_B_FRAME_SAMPLES,
  COMPONENT_B_SAMPLE_RATE_HZ,
  ComponentBFrameAssembler,
} from './componentBFrameAssembler';

describe('ComponentBFrameAssembler', () => {
  it('resamples real 100 Hz samples into one exact 960-at-64-Hz frame', () => {
    const frames: any[] = [];
    const assembler = new ComponentBFrameAssembler({onFrame: frame => frames.push(frame)});
    const epochMs = 1_787_282_838_400;
    assembler.setTemperature(33.7);

    for (let packet = 0; packet < 300; packet += 1) {
      const samples = Array.from({length: 5}, (_, index) => {
        const timestampMs = (packet * 5 + index) * 10;
        return {timestampMs, irValue: 1800 + timestampMs};
      });
      const batch: RawPpgBatch = {
        samples,
        receivedAtMs: epochMs + samples[samples.length - 1].timestampMs,
      };
      assembler.ingest(batch);
    }

    expect(frames).toHaveLength(1);
    expect(frames[0]).toMatchObject({
      timestamp: epochMs / 1000,
      sample_rate: COMPONENT_B_SAMPLE_RATE_HZ,
      temperature: 33.7,
    });
    expect(frames[0].ppg).toHaveLength(COMPONENT_B_FRAME_SAMPLES);
    expect(frames[0].ppg[0]).toBeCloseTo(1800, 6);
    expect(frames[0].ppg[959]).toBeCloseTo(1800 + 959 * (1000 / 64), 6);
  });

  it('discards a partial frame when raw acquisition has a gap', () => {
    const frames: any[] = [];
    const discontinuities: string[] = [];
    const assembler = new ComponentBFrameAssembler({
      onFrame: frame => frames.push(frame),
      onDiscontinuity: message => discontinuities.push(message),
    });

    assembler.ingest({
      receivedAtMs: 1000,
      samples: [
        {timestampMs: 0, irValue: 10},
        {timestampMs: 10, irValue: 11},
        {timestampMs: 100, irValue: 12},
      ],
    });

    expect(frames).toHaveLength(0);
    expect(discontinuities[0]).toContain('Raw PPG gap 90 ms');
  });
});
