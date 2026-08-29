import {Buffer} from 'buffer';
import {WearableTelemetry} from '../../types/domain';

type JsonRecord = Record<string, unknown>;

function finiteNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function firstNumber(source: JsonRecord, ...keys: string[]): number | null {
  for (const key of keys) {
    const value = finiteNumber(source[key]);
    if (value !== null) return value;
  }
  return null;
}

export function parseTelemetryJson(json: string): WearableTelemetry {
  let source: JsonRecord;
  try {
    const decoded: unknown = JSON.parse(json);
    if (!decoded || typeof decoded !== 'object' || Array.isArray(decoded)) throw new Error();
    source = decoded as JsonRecord;
  } catch {
    throw new Error('Malformed telemetry JSON');
  }

  const ir = firstNumber(source, 'ir');
  const red = firstNumber(source, 'red');
  const noiseAverage = firstNumber(source, 'noiseAvg', 'nAvg');
  const noisePeak = firstNumber(source, 'noisePeak', 'nPeak');
  if (ir === null || red === null || noiseAverage === null || noisePeak === null) {
    throw new Error('Telemetry packet is missing IR, RED, noiseAvg, or noisePeak');
  }

  return {
    timestampMs: firstNumber(source, 'timestampMs', 't'),
    ir,
    red,
    heartRateBpm: firstNumber(source, 'heartRateBpm', 'hr'),
    rrIntervalMs: firstNumber(source, 'rrIntervalMs', 'rr'),
    spo2: firstNumber(source, 'spo2'),
    noiseAverage,
    noisePeak,
    temperatureC: firstNumber(source, 'temperatureC', 'temp'),
    batteryPercent: firstNumber(source, 'batteryPercent', 'bat'),
    statusFlags: firstNumber(source, 'statusFlags', 'flags') ?? 0,
    receivedAt: Date.now(),
  };
}

export function parseBase64Telemetry(value: string | null): WearableTelemetry {
  if (!value) throw new Error('Telemetry notification had no value');
  return parseTelemetryJson(Buffer.from(value, 'base64').toString('utf8').replace(/\0+$/g, '').trim());
}
