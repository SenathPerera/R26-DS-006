import {
  buildDevelopmentEndpoints,
  resolvePersistedComponentBEndpoint,
} from './environment';

describe('development transport endpoints', () => {
  test('uses one reachable host for relay HTTP, relay WebSocket, and Component B', () => {
    expect(buildDevelopmentEndpoints('192.168.1.25')).toEqual({
      apiBaseUrl: 'http://192.168.1.25:8080',
      componentBIngestUrl: 'ws://192.168.1.25:8000/ingest',
      websocketUrl: 'ws://192.168.1.25:8080/realtime',
    });
  });

  test.each([
    'ws://localhost:8000/ingest',
    'ws://127.0.0.1:8000/ingest',
  ])('replaces legacy physical-device loopback endpoint %s', endpoint => {
    expect(resolvePersistedComponentBEndpoint(endpoint)).not.toBe(endpoint);
    expect(resolvePersistedComponentBEndpoint(endpoint)).toMatch(/\/ingest$/);
  });

  test('preserves an explicitly configured remote endpoint', () => {
    const endpoint = 'wss://pilot.example.org/component-b/ingest';
    expect(resolvePersistedComponentBEndpoint(endpoint)).toBe(endpoint);
  });
});
