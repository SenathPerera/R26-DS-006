import {NativeModules} from 'react-native';
import {WearableTelemetry} from '../../types/domain';

type NativeUnityBridge = {
  isAvailable?: () => Promise<boolean>;
  attachSession?: (sessionId: string, pairingCode: string) => Promise<void>;
  sendTelemetry?: (payload: string) => Promise<void>;
  pauseSession?: () => Promise<void>;
  stopSession?: () => Promise<void>;
};

const nativeBridge = NativeModules.MindSyncUnityBridge as NativeUnityBridge | undefined;

export const unityBridge = {
  async isAvailable() {
    return nativeBridge?.isAvailable ? nativeBridge.isAvailable() : false;
  },
  async attachSession(sessionId: string, pairingCode: string) {
    if (!nativeBridge?.attachSession) return;
    await nativeBridge.attachSession(sessionId, pairingCode);
  },
  async sendTelemetry(telemetry: WearableTelemetry) {
    if (!nativeBridge?.sendTelemetry) return;
    await nativeBridge.sendTelemetry(JSON.stringify(telemetry));
  },
  async pause() {
    await nativeBridge?.pauseSession?.();
  },
  async stop() {
    await nativeBridge?.stopSession?.();
  },
};
