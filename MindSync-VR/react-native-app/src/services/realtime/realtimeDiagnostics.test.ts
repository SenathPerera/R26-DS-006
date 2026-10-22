import {RealtimeService} from './realtimeService';
import type {PreparedVrSession} from '../../types/domain';

class TestSocket {
  static OPEN = 1;
  static instances: TestSocket[] = [];
  readyState = 1;
  onopen: (() => void) | null = null;
  onclose: ((event: {code: number; reason: string}) => void) | null = null;
  onerror: (() => void) | null = null;
  onmessage: ((event: {data: string}) => void) | null = null;
  send = jest.fn();
  close = jest.fn();
  constructor(_url: string) { TestSocket.instances.push(this); }
}

describe('relay operational heartbeat', () => {
  const original = globalThis.WebSocket;
  const prepared: PreparedVrSession = {
    schemaVersion: 'mindsync-session-v3', sessionId: 's1', pairingCode: '123456',
    expiresAt: 9999999999, mobileToken: 'test-only', studyCondition: 'ContextualBandit',
  };
  let service: RealtimeService;
  beforeEach(() => {
    jest.useFakeTimers(); TestSocket.instances = [];
    globalThis.WebSocket = TestSocket as unknown as typeof WebSocket;
    service = new RealtimeService();
  });
  afterEach(() => {
    service.disconnect(); jest.useRealTimers(); globalThis.WebSocket = original;
  });
  const handlers = () => ({onMessage: jest.fn(), onConnectionState: jest.fn(),
    getDiagnostics: () => ({wearableConnected: true, framesSent: 4})});

  test('sends small diagnostics after connect and stops after disconnect', () => {
    service.connectMobile(prepared, handlers());
    const socket = TestSocket.instances[0];
    expect(socket.send).not.toHaveBeenCalled();
    socket.onopen?.(); jest.advanceTimersByTime(5000);
    expect(socket.send).toHaveBeenCalledTimes(2);
    const wire = JSON.parse(socket.send.mock.calls[0][0]);
    expect(wire.schemaVersion).toBe('mindsync-monitor-v1');
    expect(wire.payload.values).toEqual({wearableConnected: true, framesSent: 4});
    expect(JSON.stringify(wire)).not.toContain('test-only');
    service.disconnect(); jest.advanceTimersByTime(15000);
    expect(socket.send).toHaveBeenCalledTimes(2);
  });

  test('a replaced socket cannot stop the new heartbeat or dispatch old data', () => {
    service.connectMobile(prepared, handlers());
    const old = TestSocket.instances[0]; old.onopen?.();
    const currentHandlers = handlers();
    service.connectMobile(prepared, currentHandlers);
    const current = TestSocket.instances[1]; current.onopen?.();
    old.onclose?.({code: 1006, reason: 'late close'});
    jest.advanceTimersByTime(5000);
    expect(current.send).toHaveBeenCalledTimes(2);
    expect(currentHandlers.onConnectionState).not.toHaveBeenCalledWith('error', expect.anything());
  });

  test('diagnostic callback failures do not break commands', () => {
    service.connectMobile(prepared, {...handlers(), getDiagnostics: () => { throw new Error('unavailable'); }});
    const socket = TestSocket.instances[0];
    expect(() => socket.onopen?.()).not.toThrow();
    service.sendCommand('pause');
    expect(JSON.parse(socket.send.mock.calls[0][0]).messageType).toBe('session_command');
  });

  test('remote close removes the heartbeat timer', () => {
    service.connectMobile(prepared, handlers());
    const socket = TestSocket.instances[0]; socket.onopen?.();
    socket.onclose?.({code: 1006, reason: 'closed'});
    jest.advanceTimersByTime(20000);
    expect(socket.send).toHaveBeenCalledTimes(1);
  });
});
