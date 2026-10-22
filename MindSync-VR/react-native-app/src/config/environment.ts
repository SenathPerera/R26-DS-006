import {generatedEnvironment} from './generatedEnvironment';
import type {StudyCondition} from '../types/domain';
export const developmentBackendHost = '192.168.43.254';

export function buildDevelopmentEndpoints(host: string) {
  const normalizedHost = host.trim();
  if (!normalizedHost || /[\s/:]/.test(normalizedHost)) {
    throw new Error('Development backend host must be a hostname or IPv4 address');
  }
  return {
    apiBaseUrl: `http://${normalizedHost}:8080`,
    componentDBaseUrl: `http://${normalizedHost}:8010`,
    componentBIngestUrl: `ws://${normalizedHost}:8000/ingest`,
    websocketUrl: `ws://${normalizedHost}:8080/realtime`,
  } as const;
}

export function resolveBackendHost(
  configuredHost: string,
  isDevelopment: boolean,
) {
  const candidate = configuredHost.trim();
  if (candidate) {
    buildDevelopmentEndpoints(candidate);
    return candidate;
  }
  if (isDevelopment) {
    return developmentBackendHost;
  }
  throw new Error(
    'Release backend is not configured. Set PILOT_BACKEND_HOST before building the APK.',
  );
}

const backendHost = resolveBackendHost(
  generatedEnvironment.pilotBackendHost,
  __DEV__,
);
const backendEndpoints = buildDevelopmentEndpoints(backendHost);

export function parseStudyCondition(value: string): StudyCondition | null {
  switch (value.trim()) {
    case 'StaticPersonalized':
    case 'RuleBasedAdaptive':
    case 'ContextualBandit':
      return value.trim() as StudyCondition;
    default:
      return null;
  }
}

export const environment = {
  apiBaseUrl: backendEndpoints.apiBaseUrl,
  componentDBaseUrl: backendEndpoints.componentDBaseUrl,
  componentBIngestUrl: backendEndpoints.componentBIngestUrl,
  websocketUrl: backendEndpoints.websocketUrl,
  useMockBackend: false,
  studyCondition: parseStudyCondition(generatedEnvironment.studyCondition),
  supabase: {
    enabled: generatedEnvironment.supabaseEnabled,
    url: generatedEnvironment.supabaseUrl,
    publishableKey: generatedEnvironment.supabasePublishableKey,
  },
} as const;

export function resolvePersistedComponentBEndpoint(endpoint?: string | null) {
  const candidate = endpoint?.trim();
  if (!candidate || /^ws:\/\/(?:localhost|127\.0\.0\.1)(?::|\/)/i.test(candidate)) {
    return environment.componentBIngestUrl;
  }
  return candidate;
}
