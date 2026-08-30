export const environment = {
  apiBaseUrl: __DEV__ ? 'http://localhost:8080' : 'https://api.mindsync.invalid',
  componentDBaseUrl: __DEV__ ? 'http://localhost:8010' : 'https://componentd.cognify.invalid',
  componentBIngestUrl: 'ws://localhost:8000/ingest',
  websocketUrl: __DEV__ ? 'ws://localhost:8080/realtime' : 'wss://api.mindsync.invalid/realtime',
  useMockBackend: true,
} as const;
