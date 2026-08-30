import {Buffer} from 'buffer';
import {RawPpgBatch, RawPpgSample} from '../../types/domain';

export const RAW_PPG_MAX_SAMPLES = 5;
export const RAW_PPG_SAMPLE_BYTES = 8;
export const RAW_PPG_PACKET_BYTES = 1 + RAW_PPG_MAX_SAMPLES * RAW_PPG_SAMPLE_BYTES;

export function parseRawPpgPacket(payload: Uint8Array, receivedAtMs = Date.now()): RawPpgBatch {
  if (payload.byteLength !== RAW_PPG_PACKET_BYTES) {
    throw new Error(`Malformed raw PPG packet: expected ${RAW_PPG_PACKET_BYTES} bytes, got ${payload.byteLength}`);
  }

  const buffer = Buffer.from(payload);
  const sampleCount = buffer.readUInt8(0);
  if (sampleCount < 1 || sampleCount > RAW_PPG_MAX_SAMPLES) {
    throw new Error(`Malformed raw PPG packet: sample_count=${sampleCount}`);
  }

  const samples: RawPpgSample[] = [];
  for (let index = 0; index < sampleCount; index += 1) {
    const offset = 1 + index * RAW_PPG_SAMPLE_BYTES;
    samples.push({
      timestampMs: buffer.readUInt32LE(offset),
      irValue: buffer.readUInt32LE(offset + 4),
    });
  }

  return {samples, receivedAtMs};
}

export function parseBase64RawPpgPacket(value: string | null, receivedAtMs = Date.now()): RawPpgBatch {
  if (!value) throw new Error('Raw PPG notification had no value');
  return parseRawPpgPacket(Buffer.from(value, 'base64'), receivedAtMs);
}
