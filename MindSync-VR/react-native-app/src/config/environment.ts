import {generatedEnvironment} from './generatedEnvironment';

export const environment = {
  apiBaseUrl: __DEV__ ? 'http://172.20.10.4:8080' : 'https://api.mindsync.invalid',
  componentDBaseUrl: __DEV__ ? 'http://localhost:8010' : 'https://componentd.cognify.invalid',
  componentBIngestUrl: 'ws://localhost:8000/ingest',
  websocketUrl: __DEV__ ? 'ws://172.20.10.4:8080/realtime' : 'wss://api.mindsync.invalid/realtime',
  useMockBackend: false,
  supabase: {
    enabled: generatedEnvironment.supabaseEnabled,
    url: generatedEnvironment.supabaseUrl,
    publishableKey: generatedEnvironment.supabasePublishableKey,
  },
} as const;
