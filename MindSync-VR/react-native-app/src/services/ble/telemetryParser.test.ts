import {Buffer} from 'buffer';
import {parseBase64Telemetry, parseTelemetryJson} from './telemetryParser';

describe('wearable telemetry parser', () => {
  it('decodes the current firmware JSON packet', () => {
    expect(parseTelemetryJson('{"ir":24500,"red":43000,"noiseAvg":85000,"noisePeak":180000}')).toMatchObject({
      ir: 24500,
      red: 43000,
      noiseAverage: 85000,
      noisePeak: 180000,
      heartRateBpm: null,
    });
  });

  it('accepts extensible compact fields without inventing values', () => {
    const value = Buffer.from('{"t":12,"ir":1,"red":2,"nAvg":3,"nPeak":4,"hr":72.5,"rr":820,"temp":31.2,"bat":90}').toString('base64');
    expect(parseBase64Telemetry(value)).toMatchObject({timestampMs: 12, heartRateBpm: 72.5, rrIntervalMs: 820, temperatureC: 31.2, batteryPercent: 90});
  });

  it('rejects incomplete packets', () => {
    expect(() => parseTelemetryJson('{"ir":1}')).toThrow('missing IR, RED, noiseAvg, or noisePeak');
  });
});
