import AsyncStorage from '@react-native-async-storage/async-storage';
import {create} from 'zustand';
import {createJSONStorage, persist} from 'zustand/middleware';
import {demoSessions, demoUser, questionnaireTemplates} from '../constants/mockData';
import {environment} from '../config/environment';
import {componentDService} from '../services/api/componentDService';
import {WEARABLE_DEVICE_NAME, wearableBleService} from '../services/ble/wearableBleService';
import {componentBPipelineService} from '../services/componentB/componentBPipelineService';
import {realtimeService} from '../services/realtime/realtimeService';
import {unityBridge} from '../services/unity/unityBridge';
import {
  BleIngestionState,
  ComponentBPipelineState,
  ConnectionState,
  MeditationSession,
  OnboardingProfile,
  QuestionnaireSubmission,
  SessionRelayState,
  UserProfile,
  VoiceCheckInState,
  VrStatus,
  WearableDevice,
  WearableTelemetry,
} from '../types/domain';

const emptyOnboarding: OnboardingProfile = {
  name: '', ageRange: '', meditationExperience: '', preferredDuration: 15, goals: [],
  meditationStyle: 'Guided', audioPreferences: [], environmentPreferences: [], sensitivities: [],
  consentAccepted: false, researchConsent: false,
};

const emptyBle: BleIngestionState = {
  isStreaming: false, telemetry: null, telemetryCount: 0, lastError: null, logs: [],
};

const emptyComponentB: ComponentBPipelineState = {
  endpoint: environment.componentBIngestUrl,
  connectionState: 'idle',
  rawCharacteristicAvailable: false,
  rawSamplesReceived: 0,
  frameSamplesBuffered: 0,
  framesQueued: 0,
  framesSent: 0,
  framesAcknowledged: 0,
  lastFrameTimestamp: null,
  lastBackendMessage: null,
  lastError: null,
  logs: [],
};

const emptyVoice: VoiceCheckInState = {
  stage: 'intro', backendHealthy: null, personName: '', language: 'english', sessionId: null, busy: false, error: null,
};

const emptyRelay: SessionRelayState = {
  connectionState: 'idle', preparedRequestId: null, preparedSession: null, questPhase: null,
  visualTelemetryMessages: [], lastError: null,
};

const developmentTemplePreference = {
  illumination: 0.319,
  warmth: 0.5,
  atmosphericSoftness: 0,
  colorRichness: 0.5,
  ambientMotion: 0.75,
};

type MindSyncStore = {
  hydrated: boolean;
  user: UserProfile | null;
  onboarding: OnboardingProfile;
  wearableDevices: WearableDevice[];
  selectedWearable: WearableDevice | null;
  wearableState: ConnectionState;
  ble: BleIngestionState;
  componentB: ComponentBPipelineState;
  vrStatus: VrStatus;
  pairingCode: string | null;
  sessions: MeditationSession[];
  activeSession: MeditationSession | null;
  sessionStatus: 'ready' | 'active' | 'paused' | 'complete';
  questionnaireSubmissions: QuestionnaireSubmission[];
  pendingValidationCount: number;
  voice: VoiceCheckInState;
  relay: SessionRelayState;
  setHydrated: (hydrated: boolean) => void;
  loginDemo: (email?: string) => void;
  signUp: (name: string, email: string) => void;
  logout: () => void;
  updateOnboarding: (patch: Partial<OnboardingProfile>) => void;
  completeOnboarding: () => void;
  scanWearables: () => Promise<void>;
  connectWearable: (device: WearableDevice) => Promise<void>;
  disconnectWearable: () => Promise<void>;
  setComponentBEndpoint: (endpoint: string) => void;
  prepareVrSession: (requestId: string) => Promise<void>;
  sendVrCommand: (command: 'pause' | 'resume' | 'stop' | 'emergency_stop') => void;
  refreshVisualLog: () => Promise<void>;
  createSession: () => MeditationSession;
  setSessionStatus: (status: MindSyncStore['sessionStatus']) => void;
  submitQuestionnaire: (templateId: string, sessionId: string | null, answers: QuestionnaireSubmission['answers']) => void;
  startVoiceCheckIn: () => Promise<void>;
  updateVoice: (patch: Partial<VoiceCheckInState>) => void;
  resetDemo: () => void;
};

export const useMindSyncStore = create<MindSyncStore>()(
  persist(
    (set, get) => ({
      hydrated: false,
      user: demoUser,
      onboarding: {...emptyOnboarding, name: demoUser.name, consentAccepted: true, researchConsent: true},
      wearableDevices: [],
      selectedWearable: null,
      wearableState: 'idle',
      ble: emptyBle,
      componentB: emptyComponentB,
      vrStatus: 'not-paired',
      pairingCode: null,
      sessions: demoSessions,
      activeSession: null,
      sessionStatus: 'ready',
      questionnaireSubmissions: [],
      pendingValidationCount: 1,
      voice: emptyVoice,
      relay: emptyRelay,
      setHydrated: hydrated => set({hydrated}),
      loginDemo: email => set({user: {...demoUser, email: email || demoUser.email}}),
      signUp: (name, email) => set({user: {...demoUser, id: `participant-${Date.now()}`, name, email, onboardingComplete: false}, onboarding: {...emptyOnboarding, name}}),
      logout: () => set({user: null, activeSession: null, sessionStatus: 'ready'}),
      updateOnboarding: patch => set(state => ({onboarding: {...state.onboarding, ...patch}})),
      completeOnboarding: () => set(state => ({user: {...(state.user ?? demoUser), name: state.onboarding.name || state.user?.name || demoUser.name, onboardingComplete: true}})),
      scanWearables: async () => { await wearableBleService.scan(); },
      connectWearable: async device => {
        set({selectedWearable: device});
        try { await wearableBleService.connect(device.id); } catch { /* state is reported by callbacks */ }
      },
      disconnectWearable: async () => { await wearableBleService.disconnect(); },
      setComponentBEndpoint: endpoint => componentBPipelineService.setEndpoint(endpoint),
      prepareVrSession: async requestId => {
        const state = get();
        const bindMobileRelay = (prepared: NonNullable<SessionRelayState['preparedSession']>) => {
          realtimeService.connectMobile(prepared, {
            onConnectionState: (connectionState, error) => set(current => ({
              relay: {...current.relay, connectionState, lastError: error ?? null},
              vrStatus: connectionState === 'error' ? 'disconnected' : current.vrStatus,
            })),
            onMessage: message => set(current => {
              if (message.messageType === 'quest_state') {
                const phase = typeof message.payload.phase === 'string' ? message.payload.phase : null;
                return {
                  relay: {...current.relay, questPhase: phase},
                  vrStatus: phase === 'adaptive' ? 'active' : 'ready',
                };
              }
              if (message.messageType === 'visual_telemetry_batch') {
                return {relay: {
                  ...current.relay,
                  visualTelemetryMessages: [...current.relay.visualTelemetryMessages, message].slice(-200),
                }};
              }
              return {};
            }),
          });
        };
        const prepared = state.relay.preparedSession;
        const codeIsFresh = prepared != null && prepared.expiresAt > Date.now() / 1000;
        if (prepared && state.relay.preparedRequestId === requestId && codeIsFresh) {
          if (state.relay.connectionState !== 'connected' && state.relay.connectionState !== 'connecting') {
            set({relay: {...state.relay, connectionState: 'connecting', lastError: null}});
            bindMobileRelay(prepared);
          }
          return;
        }
        set({vrStatus: 'pairing', relay: {...emptyRelay, connectionState: 'connecting'}});
        try {
          const createRequestId = prepared && !codeIsFresh ? `${requestId}-${Date.now()}` : requestId;
          const newPrepared = await realtimeService.createPreparedSession({
            requestId: createRequestId,
            participantPseudonym: state.user?.id ?? 'anonymous',
            sceneId: 'temple-pond',
            preferredEnvironment: developmentTemplePreference,
          });
          const activeSession = state.activeSession ?? get().createSession();
          set({
            activeSession: {...activeSession, id: newPrepared.sessionId},
            pairingCode: newPrepared.pairingCode,
            vrStatus: 'waiting',
            relay: {...emptyRelay, preparedRequestId: requestId, preparedSession: newPrepared, connectionState: 'connecting'},
          });
          bindMobileRelay(newPrepared);
        } catch (error) {
          const lastError = error instanceof Error ? error.message : 'relay-session-create-failed';
          set({vrStatus: 'not-paired', relay: {...emptyRelay, connectionState: 'error', lastError}});
          throw error;
        }
      },
      sendVrCommand: command => {
        try {
          realtimeService.sendCommand(command);
        } catch (error) {
          set(state => ({relay: {...state.relay, lastError: error instanceof Error ? error.message : 'relay-command-failed'}}));
        }
      },
      refreshVisualLog: async () => {
        const prepared = get().relay.preparedSession;
        if (!prepared) return;
        try {
          const visualTelemetryMessages = await realtimeService.fetchVisualLog(prepared);
          set(state => ({relay: {...state.relay, visualTelemetryMessages, lastError: null}}));
        } catch (error) {
          set(state => ({relay: {...state.relay, lastError: error instanceof Error ? error.message : 'relay-log-download-failed'}}));
          throw error;
        }
      },
      createSession: () => {
        const session: MeditationSession = {
          id: `session-${Date.now()}`, title: 'Japanese Temple Pond Garden', date: new Date().toISOString().slice(0, 10),
          durationMinutes: 20, environment: 'Temple Pond', audioProfile: 'Adaptive audio',
          completionRate: 0, moodBefore: 5, moodAfter: 0, validationComplete: false,
        };
        set({activeSession: session, sessionStatus: 'ready'});
        return session;
      },
      setSessionStatus: status => set({sessionStatus: status}),
      submitQuestionnaire: (templateId, sessionId, answers) => {
        const submission: QuestionnaireSubmission = {
          id: `response-${Date.now()}`, templateId, sessionId, userId: get().user?.id ?? 'anonymous',
          submittedAt: new Date().toISOString(), synced: false, exportShapeVersion: 'component-d-v1', answers,
        };
        set(state => ({questionnaireSubmissions: [submission, ...state.questionnaireSubmissions], pendingValidationCount: Math.max(0, state.pendingValidationCount - 1)}));
      },
      startVoiceCheckIn: async () => {
        set({voice: {...emptyVoice, backendHealthy: null, personName: get().user?.name ?? '', sessionId: `voice-${Date.now()}`, busy: true}});
        try {
          await componentDService.health();
          set(state => ({voice: {...state.voice, backendHealthy: true, busy: false}}));
        } catch (error) {
          set(state => ({voice: {...state.voice, backendHealthy: false, busy: false, error: error instanceof Error ? error.message : 'Component D unavailable'}}));
        }
      },
      updateVoice: patch => set(state => ({voice: {...state.voice, ...patch}})),
      resetDemo: () => { realtimeService.disconnect(); set({user: demoUser, onboarding: {...emptyOnboarding, name: demoUser.name, consentAccepted: true, researchConsent: true}, sessions: demoSessions, questionnaireSubmissions: [], pendingValidationCount: 1, vrStatus: 'not-paired', pairingCode: null, activeSession: null, voice: emptyVoice, relay: emptyRelay}); },
    }),
    {
      name: 'mindsync-rn-state-v1',
      storage: createJSONStorage(() => AsyncStorage),
      partialize: state => ({user: state.user, onboarding: state.onboarding, sessions: state.sessions, questionnaireSubmissions: state.questionnaireSubmissions, pendingValidationCount: state.pendingValidationCount, componentB: {...emptyComponentB, endpoint: state.componentB.endpoint}}),
      onRehydrateStorage: () => state => {
        state?.setHydrated(true);
        componentBPipelineService.setEndpoint(state?.componentB.endpoint ?? environment.componentBIngestUrl);
      },
    },
  ),
);

function appendLog(message: string) {
  const line = `${new Date().toLocaleTimeString()}  ${message}`;
  useMindSyncStore.setState(state => ({ble: {...state.ble, logs: [line, ...state.ble.logs].slice(0, 40)}}));
}

function appendComponentBLog(message: string) {
  const line = `${new Date().toLocaleTimeString()}  ${message}`;
  useMindSyncStore.setState(state => ({componentB: {...state.componentB, logs: [line, ...state.componentB.logs].slice(0, 40)}}));
}

wearableBleService.configure({
  onState: wearableState => useMindSyncStore.setState(state => ({
    wearableState,
    selectedWearable: wearableState === 'connected' && state.selectedWearable
      ? {...state.selectedWearable, name: WEARABLE_DEVICE_NAME, verified: true, firmware: 'ESP32-S3 Mini'}
      : state.selectedWearable,
    ble: {...state.ble, isStreaming: wearableState === 'connected' && state.ble.telemetry !== null},
  })),
  onDevices: wearableDevices => useMindSyncStore.setState({wearableDevices}),
  onTelemetry: (telemetry: WearableTelemetry) => {
    useMindSyncStore.setState(state => ({ble: {...state.ble, telemetry, telemetryCount: state.ble.telemetryCount + 1, isStreaming: true, lastError: null}}));
    componentBPipelineService.acceptTelemetry(telemetry);
    void unityBridge.sendTelemetry(telemetry);
  },
  onRawPpg: batch => componentBPipelineService.acceptRawPpg(batch),
  onRawPpgAvailability: available => componentBPipelineService.setRawCharacteristicAvailable(available),
  onError: lastError => useMindSyncStore.setState(state => ({ble: {...state.ble, lastError}})),
  onLog: appendLog,
});

componentBPipelineService.configure({
  onState: patch => useMindSyncStore.setState(state => ({componentB: {...state.componentB, ...patch}})),
  onLog: appendComponentBLog,
});
componentBPipelineService.setEndpoint(useMindSyncStore.getState().componentB.endpoint);

useMindSyncStore.subscribe((state, previousState) => {
  if (state.wearableState === previousState.wearableState) return;
  if (state.wearableState === 'connected') componentBPipelineService.start();
  if (state.wearableState === 'disconnected' || state.wearableState === 'error') componentBPipelineService.stop();
});

export {questionnaireTemplates};
