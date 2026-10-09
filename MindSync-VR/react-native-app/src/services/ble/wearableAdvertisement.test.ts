import {
  mergeWearableAdvertisement,
  rankWearableCandidates,
  WEARABLE_DEVICE_NAME,
  WEARABLE_SERVICE_UUID,
} from './wearableAdvertisement';

describe('wearable BLE advertisement handling', () => {
  it('keeps anonymous advertisements available for GATT verification regardless of RSSI', () => {
    const candidate = mergeWearableAdvertisement(undefined, {
      id: 'AA:BB:CC:DD:EE:FF',
      localName: null,
      name: null,
      serviceUUIDs: null,
      rssi: -92,
    });

    expect(candidate.name).toBe('Unknown BLE device EE:FF');
    expect(candidate.rssi).toBe(-92);
    expect(candidate.verified).toBe(false);
  });

  it('upgrades an anonymous candidate when a later scan response contains the wearable name', () => {
    const anonymous = mergeWearableAdvertisement(undefined, {
      id: 'device-1',
      localName: null,
      name: null,
      serviceUUIDs: null,
      rssi: -80,
    });
    const verified = mergeWearableAdvertisement(anonymous, {
      id: 'device-1',
      localName: WEARABLE_DEVICE_NAME,
      name: null,
      serviceUUIDs: null,
      rssi: -75,
    });

    expect(verified.name).toBe(WEARABLE_DEVICE_NAME);
    expect(verified.rssi).toBe(-75);
    expect(verified.verified).toBe(true);
  });

  it('recognizes the wearable service UUID case-insensitively and ranks it first', () => {
    const wearable = mergeWearableAdvertisement(undefined, {
      id: 'wearable',
      localName: null,
      name: null,
      serviceUUIDs: [WEARABLE_SERVICE_UUID.toUpperCase()],
      rssi: -90,
    });
    const nearby = mergeWearableAdvertisement(undefined, {
      id: 'nearby',
      localName: 'Nearby device',
      name: null,
      serviceUUIDs: null,
      rssi: -30,
    });

    expect(rankWearableCandidates([nearby, wearable])).toEqual([wearable, nearby]);
  });
});
