import {Buffer} from 'buffer';
import {parseBase64RawPpgPacket, parseRawPpgPacket, RAW_PPG_PACKET_BYTES} from './rawPpgParser';

function packet(sampleCount: number) {
  const bytes = Buffer.alloc(RAW_PPG_PACKET_BYTES);
  bytes.writeUInt8(sampleCount, 0);
  for (let index = 0; index < 5; index += 1) {
    const offset = 1 + index * 8;
    bytes.writeUInt32LE(1000 + index * 10, offset);
    bytes.writeUInt32LE(24000 + index, offset + 4);
  }
  return bytes;
}

describe('raw PPG packet parser', () => {
  it('decodes little-endian samples and ignores unused slots', () => {
    const decoded = parseBase64RawPpgPacket(packet(3).toString('base64'), 1234);

    expect(decoded).toEqual({
      receivedAtMs: 1234,
      samples: [
        {timestampMs: 1000, irValue: 24000},
        {timestampMs: 1010, irValue: 24001},
        {timestampMs: 1020, irValue: 24002},
      ],
    });
  });

  it('rejects incomplete and invalid packets', () => {
    expect(() => parseRawPpgPacket(packet(5).subarray(0, 20))).toThrow('expected 41 bytes');
    expect(() => parseRawPpgPacket(packet(6))).toThrow('sample_count=6');
    expect(() => parseRawPpgPacket(packet(0))).toThrow('sample_count=0');
  });
});
