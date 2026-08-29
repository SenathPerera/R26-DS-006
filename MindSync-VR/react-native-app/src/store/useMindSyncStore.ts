import AsyncStorage from '@react-native-async-storage/async-storage';
import {create} from 'zustand';
import {createJSONStorage, persist} from 'zustand/middleware';
import {demoSessions, demoUser, questionnaireTemplates} from '../constants/mockData';
import {componentDService} from '../services/api/componentDService';
import {WEARABLE_DEVICE_NAME, wearableBleService} from '../services/ble/wearableBleService';
import {unityBridge} from '../services/unity/unityBridge';
import {
  BleIngestionState,
  ConnectionState,
  MeditationSession,
  OnboardingProfile,
  QuestionnaireSubmission,
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

const emptyVoice: VoiceCheckInState = {
  stage: 'intro', backendHealthy: null, personName: '', language: 'english', sessionId: null, busy: false, error: null,
};

type MindSyncStore = {
  hydrated: boolean;
  user: UserProfile | null;
  onboarding: OnboardingProfile;
  wearableDevices: WearableDevice[];
  selectedWearable: WearableDevice | null;
  wearableState: ConnectionState;
  ble: BleIngestionState;
  vrStatus: VrStatus;
  pairingCode: string | null;
  sessions: MeditationSession[];
  activeSession: MeditationSession | null;
  sessionStatus: 'ready' | 'active' | 'paused' | 'complete';
  questionnaireSubmissions: QuestionnaireSubmission[];
  pendingValidationCount: number;
  voice: VoiceCheckInState;
  setHydrated: (hydrated: boolean) => void;
  loginDemo: (email?: string) => void;
  signUp: (name: string, email: string) => void;
  logout: () => void;
  updateOnboarding: (patch: Partial<OnboardingProfile>) => void;
  completeOnboarding: () => void;
  scanWearables: () => Promise<void>;
  connectWearable: (device: WearableDevice) => Promise<void>;
  disconnectWearable: () => Promise<void>;
  pairVr: () => void;
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
      vrStatus: 'not-paired',
      pairingCode: null,
      sessions: demoSessions,
      activeSession: null,
      sessionStatus: 'ready',
      questionnaireSubmissions: [],
      pendingValidationCount: 1,
      voice: emptyVoice,
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
      pairVr: () => set({vrStatus: 'ready', pairingCode: `MSVR-${Math.floor(1000 + Math.random() * 9000)}`}),
      createSession: () => {
        const session: MeditationSession = {
          id: `session-${Date.now()}`, title: 'Adaptive Ocean Reset', date: new Date().toISOString().slice(0, 10),
          durationMinutes: get().onboarding.preferredDuration || 15, environment: 'Ocean', audioProfile: 'Warm pads',
          completionRate: 0, moodBefore: 5, moodAfter: 0, validationComplete: false,
        };
        set({activeSession: session, sessionStatus: 'ready'});
        void unityBridge.attachSession(session.id, get().pairingCode ?? 'UNPAIRED');
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
      resetDemo: () => set({user: demoUser, onboarding: {...emptyOnboarding, name: demoUser.name, consentAccepted: true, researchConsent: true}, sessions: demoSessions, questionnaireSubmissions: [], pendingValidationCount: 1, vrStatus: 'not-paired', pairingCode: null, activeSession: null, voice: emptyVoice}),
    }),
    {
      name: 'mindsync-rn-state-v1',
      storage: createJSONStorage(() => AsyncStorage),
      partialize: state => ({user: state.user, onboarding: state.onboarding, sessions: state.sessions, questionnaireSubmissions: state.questionnaireSubmissions, pendingValidationCount: state.pendingValidationCount}),
      onRehydrateStorage: () => state => state?.setHydrated(true),
    },
  ),
);

function appendLog(message: string) {
  const line = `${new Date().toLocaleTimeString()}  ${message}`;
  useMindSyncStore.setState(state => ({ble: {...state.ble, logs: [line, ...state.ble.logs].slice(0, 40)}}));
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
    void unityBridge.sendTelemetry(telemetry);
  },
  onError: lastError => useMindSyncStore.setState(state => ({ble: {...state.ble, lastError}})),
  onLog: appendLog,
});

export {questionnaireTemplates};
