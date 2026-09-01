export const developmentBackendHost = '192.168.183.190';

export function buildDevelopmentEndpoints(host: string) {
  const normalizedHost = host.trim();
  if (!normalizedHost || /[\s/:]/.test(normalizedHost)) {
    throw new Error('Development backend host must be a hostname or IPv4 address');
  }
  return {
    apiBaseUrl: `http://${normalizedHost}:8080`,
    componentBIngestUrl: `ws://${normalizedHost}:8000/ingest`,
    websocketUrl: `ws://${normalizedHost}:8080/realtime`,
  } as const;
}

const developmentEndpoints = buildDevelopmentEndpoints(developmentBackendHost);

export const environment = {
  apiBaseUrl: __DEV__ ? developmentEndpoints.apiBaseUrl : 'https://api.mindsync.invalid',
  componentDBaseUrl: __DEV__ ? 'http://localhost:8010' : 'https://componentd.cognify.invalid',
  componentBIngestUrl: __DEV__ ? developmentEndpoints.componentBIngestUrl : 'wss://api.mindsync.invalid/component-b/ingest',
  websocketUrl: __DEV__ ? developmentEndpoints.websocketUrl : 'wss://api.mindsync.invalid/realtime',
  useMockBackend: false,
} as const;

export function resolvePersistedComponentBEndpoint(endpoint?: string | null) {
  const candidate = endpoint?.trim();
  if (!candidate || /^ws:\/\/(?:localhost|127\.0\.0\.1)(?::|\/)/i.test(candidate)) {
    return environment.componentBIngestUrl;
  }
  return candidate;
}
