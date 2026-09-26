import {PermissionsAndroid, Platform} from 'react-native';
import {
  BleError,
  BleManager,
  Characteristic,
  Device,
  State,
  Subscription,
} from 'react-native-ble-plx';
import {ConnectionState, RawPpgBatch, WearableDevice, WearableTelemetry} from '../../types/domain';
import {parseBase64RawPpgPacket} from './rawPpgParser';
import {parseBase64Telemetry} from './telemetryParser';

export const WEARABLE_DEVICE_NAME = 'WearableHealthMonitor';
export const WEARABLE_SERVICE_UUID = '7c69f001-7f70-4b0a-9c91-93d7f91b1001';
export const WEARABLE_TELEMETRY_UUID = '7c69f002-7f70-4b0a-9c91-93d7f91b1001';
export const WEARABLE_RAW_PPG_UUID = '7c69f003-7f70-4b0a-9c91-93d7f91b1001';

type Callbacks = {
  onState: (state: ConnectionState) => void;
  onDevices: (devices: WearableDevice[]) => void;
  onTelemetry: (telemetry: WearableTelemetry) => void;
  onRawPpg: (batch: RawPpgBatch) => void;
  onRawPpgAvailability: (available: boolean) => void;
  onError: (message: string | null) => void;
  onLog: (message: string) => void;
};

const SCAN_MS = 9000;
const MAX_RECONNECT_ATTEMPTS = 4;

function normalizeUuid(value: string) {
  return value.toLowerCase();
}

function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  return 'Unexpected Bluetooth error';
}

export class WearableBleService {
  private readonly manager = new BleManager();
  private telemetryMonitor: Subscription | null = null;
  private rawPpgMonitor: Subscription | null = null;
  private disconnectMonitor: Subscription | null = null;
  private scanTimer: ReturnType<typeof setTimeout> | null = null;
  private connected: Device | null = null;
  private callbacks: Callbacks | null = null;
  private reconnectEnabled = false;
  private reconnectAttempt = 0;
  private destroyed = false;

  configure(callbacks: Callbacks) {
    this.callbacks = callbacks;
  }

  private log(message: string) {
    this.callbacks?.onLog(message);
    console.info(`[MindSync BLE] ${message}`);
  }

  private fail(message: string) {
    this.callbacks?.onError(message);
    this.callbacks?.onState('error');
    this.log(message);
  }

  async requestPermissions(): Promise<boolean> {
    if (Platform.OS !== 'android') return true;
    if (Number(Platform.Version) >= 31) {
      const result = await PermissionsAndroid.requestMultiple([
        PermissionsAndroid.PERMISSIONS.BLUETOOTH_SCAN,
        PermissionsAndroid.PERMISSIONS.BLUETOOTH_CONNECT,
      ]);
      return result[PermissionsAndroid.PERMISSIONS.BLUETOOTH_SCAN] === PermissionsAndroid.RESULTS.GRANTED &&
        result[PermissionsAndroid.PERMISSIONS.BLUETOOTH_CONNECT] === PermissionsAndroid.RESULTS.GRANTED;
    }
    return (await PermissionsAndroid.request(PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION)) === PermissionsAndroid.RESULTS.GRANTED;
  }

  private async ensureReady() {
    if (!(await this.requestPermissions())) throw new Error('Bluetooth permission was not granted');
    const state = await this.manager.state();
    if (state !== State.PoweredOn) {
      throw new Error(state === State.PoweredOff ? 'Bluetooth is turned off' : `Bluetooth is unavailable (${state})`);
    }
  }

  async scan(): Promise<WearableDevice[]> {
    try {
      await this.ensureReady();
      this.stopScan();
      this.callbacks?.onError(null);
      this.callbacks?.onState('scanning');
      this.log(`Scanning for ${WEARABLE_DEVICE_NAME} and service ${WEARABLE_SERVICE_UUID}`);

      const found = new Map<string, WearableDevice>();
      return await new Promise(resolve => {
        const finish = () => {
          this.stopScan();
          const devices = [...found.values()]
            .filter(device => device.verified || device.rssi >= -78)
            .sort((a, b) => Number(b.verified) - Number(a.verified) || b.rssi - a.rssi)
            .slice(0, 8);
          this.callbacks?.onDevices(devices);
          this.callbacks?.onState('idle');
          if (devices.length === 0) {
            this.callbacks?.onError(`${WEARABLE_DEVICE_NAME} not found. Confirm the wearable is powered and advertising.`);
            this.log('No suitable BLE candidates found');
          } else if (!devices.some(device => device.verified)) {
            this.log('Wearable advertisement name/service is hidden; connect to verify a strong candidate');
          }
          resolve(devices);
        };

        this.manager.startDeviceScan(null, {allowDuplicates: false}, (error, device) => {
          if (error) {
            this.log(`Scan error: ${error.message}`);
            finish();
            return;
          }
          if (!device) return;
          const advertisedName = device.localName ?? device.name;
          const serviceUuids = (device.serviceUUIDs ?? []).map(normalizeUuid);
          const verified = advertisedName === WEARABLE_DEVICE_NAME || serviceUuids.includes(WEARABLE_SERVICE_UUID);
          const rssi = device.rssi ?? -127;
          if (!verified && rssi < -78) return;
          const candidate: WearableDevice = {
            id: device.id,
            name: advertisedName ?? `Unknown BLE device ${device.id.slice(-5)}`,
            rssi,
            verified,
            firmware: verified ? 'ESP32-S3 Mini' : 'Unverified; connect to inspect GATT',
          };
          found.set(device.id, candidate);
          const sorted = [...found.values()].sort((a, b) => Number(b.verified) - Number(a.verified) || b.rssi - a.rssi);
          this.callbacks?.onDevices(sorted);
          this.log(`Saw ${candidate.name} id=${device.id} rssi=${rssi} verified=${verified}`);
        });
        this.scanTimer = setTimeout(finish, SCAN_MS);
      });
    } catch (error) {
      this.fail(errorMessage(error));
      return [];
    }
  }

  stopScan() {
    this.manager.stopDeviceScan();
    if (this.scanTimer) clearTimeout(this.scanTimer);
    this.scanTimer = null;
  }

  async connect(deviceId: string, isReconnect = false): Promise<void> {
    try {
      await this.ensureReady();
      this.stopScan();
      this.callbacks?.onError(null);
      this.callbacks?.onState('connecting');
      this.log(`${isReconnect ? 'Reconnecting' : 'Connecting'} to ${deviceId}`);

      this.telemetryMonitor?.remove();
      this.rawPpgMonitor?.remove();
      this.disconnectMonitor?.remove();
      const connected = await this.manager.connectToDevice(deviceId, {autoConnect: false, timeout: 12000});
      this.connected = connected;
      this.reconnectEnabled = true;
      try {
        await connected.requestMTU(185);
      } catch (error) {
        this.log(`MTU request was not accepted; continuing with default MTU (${errorMessage(error)})`);
      }
      const discovered = await connected.discoverAllServicesAndCharacteristics();
      const rawPpgAvailable = await this.verifyWearableGatt(discovered);
      this.startMonitoring(discovered, rawPpgAvailable);
      this.callbacks?.onRawPpgAvailability(rawPpgAvailable);
      this.watchDisconnect(discovered.id);
      this.reconnectAttempt = 0;
      this.callbacks?.onState('connected');
      this.log(`Wearable connected; telemetry subscribed${rawPpgAvailable ? ' with raw PPG' : ' (raw PPG unavailable)'}`);
    } catch (error) {
      const message = errorMessage(error);
      this.connected = null;
      this.fail(message);
      throw error;
    }
  }

  private async verifyWearableGatt(device: Device) {
    const services = await device.services();
    const service = services.find(item => normalizeUuid(item.uuid) === WEARABLE_SERVICE_UUID);
    if (!service) {
      this.log(`GATT services: ${services.map(item => item.uuid).join(', ') || '<none>'}`);
      throw new Error('Selected device is not the MindSync wearable service');
    }
    const characteristics = await device.characteristicsForService(service.uuid);
    const telemetry = characteristics.find(item => normalizeUuid(item.uuid) === WEARABLE_TELEMETRY_UUID);
    if (!telemetry || !telemetry.isNotifiable) {
      this.log(`Wearable characteristics: ${characteristics.map(item => item.uuid).join(', ') || '<none>'}`);
      throw new Error('Wearable telemetry characteristic was not found or is not notifiable');
    }
    const rawPpg = characteristics.find(item => normalizeUuid(item.uuid) === WEARABLE_RAW_PPG_UUID);
    if (!rawPpg || !rawPpg.isNotifiable) {
      this.log('Raw PPG characteristic not found; update the ESP32 firmware to enable Component B relay');
      return false;
    }
    return true;
  }

  private startMonitoring(device: Device, rawPpgAvailable: boolean) {
    this.telemetryMonitor = device.monitorCharacteristicForService(
      WEARABLE_SERVICE_UUID,
      WEARABLE_TELEMETRY_UUID,
      (error: BleError | null, characteristic: Characteristic | null) => {
        if (error) {
          this.callbacks?.onError(`Telemetry subscription error: ${error.message}`);
          this.log(`Notification error: ${error.message}`);
          return;
        }
        try {
          this.callbacks?.onTelemetry(parseBase64Telemetry(characteristic?.value ?? null));
          this.callbacks?.onError(null);
        } catch (parseError) {
          const message = errorMessage(parseError);
          this.callbacks?.onError(message);
          this.log(`Ignored malformed notification: ${message}`);
        }
      },
    );

    if (rawPpgAvailable) {
      this.rawPpgMonitor = device.monitorCharacteristicForService(
        WEARABLE_SERVICE_UUID,
        WEARABLE_RAW_PPG_UUID,
        (error: BleError | null, characteristic: Characteristic | null) => {
          if (error) {
            this.log(`Raw PPG subscription error: ${error.message}`);
            return;
          }
          try {
            this.callbacks?.onRawPpg(parseBase64RawPpgPacket(characteristic?.value ?? null));
          } catch (parseError) {
            this.log(`Ignored malformed raw PPG notification: ${errorMessage(parseError)}`);
          }
        },
      );
    }
  }

  private watchDisconnect(deviceId: string) {
    this.disconnectMonitor = this.manager.onDeviceDisconnected(deviceId, error => {
      this.telemetryMonitor?.remove();
      this.telemetryMonitor = null;
      this.rawPpgMonitor?.remove();
      this.rawPpgMonitor = null;
      this.connected = null;
      this.callbacks?.onRawPpgAvailability(false);
      this.callbacks?.onState('disconnected');
      this.log(error ? `Wearable disconnected: ${error.message}` : 'Wearable disconnected');
      if (this.reconnectEnabled && !this.destroyed) void this.reconnect(deviceId);
    });
  }

  private async reconnect(deviceId: string) {
    if (this.reconnectAttempt >= MAX_RECONNECT_ATTEMPTS) {
      this.fail('Automatic reconnect stopped. Tap reconnect when the wearable is available.');
      return;
    }
    this.reconnectAttempt += 1;
    const delayMs = Math.min(2000 * this.reconnectAttempt, 8000);
    this.log(`Reconnect attempt ${this.reconnectAttempt}/${MAX_RECONNECT_ATTEMPTS} in ${delayMs / 1000}s`);
    await new Promise<void>(resolve => setTimeout(() => resolve(), delayMs));
    if (!this.reconnectEnabled || this.destroyed) return;
    try {
      await this.connect(deviceId, true);
    } catch {
      if (this.reconnectEnabled) void this.reconnect(deviceId);
    }
  }

  async disconnect() {
    this.reconnectEnabled = false;
    this.reconnectAttempt = 0;
    this.stopScan();
    this.telemetryMonitor?.remove();
    this.rawPpgMonitor?.remove();
    this.disconnectMonitor?.remove();
    const current = this.connected;
    this.connected = null;
    this.callbacks?.onRawPpgAvailability(false);
    if (current) {
      try {
        await this.manager.cancelDeviceConnection(current.id);
      } catch (error) {
        this.log(`Disconnect cleanup: ${errorMessage(error)}`);
      }
    }
    this.callbacks?.onState('disconnected');
    this.log('Wearable disconnected by user');
  }

  destroy() {
    this.destroyed = true;
    this.reconnectEnabled = false;
    this.stopScan();
    this.telemetryMonitor?.remove();
    this.rawPpgMonitor?.remove();
    this.disconnectMonitor?.remove();
    this.manager.destroy();
  }
}

export const wearableBleService = new WearableBleService();
