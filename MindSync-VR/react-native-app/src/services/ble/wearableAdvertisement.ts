import {WearableDevice} from '../../types/domain';

export const WEARABLE_DEVICE_NAME = 'WearableHealthMonitor';
export const WEARABLE_SERVICE_UUID = '7c69f001-7f70-4b0a-9c91-93d7f91b1001';
export const WEARABLE_TELEMETRY_UUID = '7c69f002-7f70-4b0a-9c91-93d7f91b1001';
export const WEARABLE_RAW_PPG_UUID = '7c69f003-7f70-4b0a-9c91-93d7f91b1001';

export interface BleAdvertisement {
  id: string;
  localName: string | null;
  name: string | null;
  serviceUUIDs: string[] | null;
  rssi: number | null;
}

function normalizeUuid(value: string) {
  return value.toLowerCase();
}

export function mergeWearableAdvertisement(
  previous: WearableDevice | undefined,
  advertisement: BleAdvertisement,
): WearableDevice {
  const advertisedName = advertisement.localName ?? advertisement.name;
  const serviceUuids = (advertisement.serviceUUIDs ?? []).map(normalizeUuid);
  const verifiedNow = advertisedName === WEARABLE_DEVICE_NAME ||
    serviceUuids.includes(WEARABLE_SERVICE_UUID);
  const verified = previous?.verified === true || verifiedNow;

  return {
    id: advertisement.id,
    name: advertisedName ?? previous?.name ?? `Unknown BLE device ${advertisement.id.slice(-5)}`,
    rssi: advertisement.rssi ?? previous?.rssi ?? -127,
    verified,
    firmware: verified ? 'ESP32-S3 Mini' : 'Unverified; connect to inspect GATT',
  };
}

export function rankWearableCandidates(devices: Iterable<WearableDevice>, limit = 8) {
  return [...devices]
    .sort((a, b) => Number(b.verified) - Number(a.verified) || b.rssi - a.rssi)
    .slice(0, limit);
}
