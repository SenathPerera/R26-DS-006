import AsyncStorage from '@react-native-async-storage/async-storage';
import type {Session} from '@supabase/supabase-js';
import {create} from 'zustand';
import {createJSONStorage, persist} from 'zustand/middleware';
import {demoSessions, demoUser, questionnaireTemplates} from '../constants/mockData';
import {environment} from '../config/environment';
import {componentDService} from '../services/api/componentDService';
import {WEARABLE_DEVICE_NAME, wearableBleService} from '../services/ble/wearableBleService';
import {componentBPipelineService} from '../services/componentB/componentBPipelineService';
import {realtimeService} from '../services/realtime/realtimeService';
import type {VisualLogSnapshot} from '../services/realtime/realtimeService';
import {unityBridge} from '../services/unity/unityBridge';
import {sessionRecordOutbox} from '../services/session/sessionRecordOutbox';
import {isSupabaseConfigured} from '../services/supabase/supabaseClient';
import {supabaseAuthService} from '../services/supabase/supabaseAuthService';
import {mindSyncRepository} from '../services/supabase/mindSyncRepository';
import {
  AuthStatus,
  BleIngestionState,
  ComponentBPipelineState,
  ConnectionState,
  DataSyncStatus,
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
  visualTelemetryMessages: [], visualLogSnapshot: null,
  visualLogDeliveryStatus: 'idle', visualLogMessageCount: 0,
  lastError: null,
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
  authStatus: AuthStatus;
  authError: string | null;
  dataSyncStatus: DataSyncStatus;
  dataSyncError: string | null;
  lastSyncedAt: string | null;
  supabaseConfigured: boolean;
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
  initializeAuth: () => Promise<() => void>;
  login: (email: string, password: string) => Promise<void>;
  signUp: (name: string, email: string, password: string) => Promise<{emailConfirmationRequired: boolean}>;
  sendPasswordReset: (email: string) => Promise<void>;
  logout: () => Promise<void>;
  syncNow: () => Promise<void>;
  updateOnboarding: (patch: Partial<OnboardingProfile>) => void;
  completeOnboarding: () => Promise<void>;
  scanWearables: () => Promise<void>;
  connectWearable: (device: WearableDevice) => Promise<void>;
  disconnectWearable: () => Promise<void>;
  setComponentBEndpoint: (endpoint: string) => void;
  prepareVrSession: (requestId: string) => Promise<void>;
  sendVrCommand: (command: 'pause' | 'resume' | 'stop' | 'emergency_stop') => void;
  refreshVisualLog: () => Promise<VisualLogSnapshot | null>;
  createSession: () => MeditationSession;
  setSessionStatus: (status: MindSyncStore['sessionStatus']) => void;
  submitQuestionnaire: (templateId: string, sessionId: string | null, answers: QuestionnaireSubmission['answers']) => Promise<void>;
  startVoiceCheckIn: () => Promise<void>;
  updateVoice: (patch: Partial<VoiceCheckInState>) => void;
  resetDemo: () => void;
};

export const useMindSyncStore = create<MindSyncStore>()(
  persist(
    (set, get) => ({
      hydrated: false,
      authStatus: 'initializing',
      authError: null,
      dataSyncStatus: 'idle',
      dataSyncError: null,
      lastSyncedAt: null,
      supabaseConfigured: isSupabaseConfigured,
      user: null,
      onboarding: emptyOnboarding,
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
      initializeAuth: async () => {
        if (!isSupabaseConfigured) {
          set({authStatus: 'signed-out', authError: null, user: null});
          return () => undefined;
        }
        set({authStatus: 'initializing', authError: null});
        try {
          return await supabaseAuthService.initialize(applySupabaseSession);
        } catch (error) {
          set({authStatus: 'error', authError: errorMessage(error)});
          return () => undefined;
        }
      },
      login: async (email, password) => {
        set({authStatus: 'authenticating', authError: null});
        if (!isSupabaseConfigured) {
          set({
            authStatus: 'authenticated',
            user: {...demoUser, email: email || demoUser.email},
            onboarding: {...emptyOnboarding, name: demoUser.name, consentAccepted: true, researchConsent: true},
            sessions: demoSessions,
          });
          return;
        }
        try {
          const session = await supabaseAuthService.signIn(email, password);
          await applySupabaseSession(session);
        } catch (error) {
          set({authStatus: 'error', authError: errorMessage(error), user: null});
          throw error;
        }
      },
      signUp: async (name, email, password) => {
        set({authStatus: 'authenticating', authError: null});
        if (!isSupabaseConfigured) {
          set({
            authStatus: 'authenticated',
            user: {...demoUser, id: `participant-${Date.now()}`, name, email, onboardingComplete: false},
            onboarding: {...emptyOnboarding, name},
            sessions: [],
          });
          return {emailConfirmationRequired: false};
        }
        try {
          const result = await supabaseAuthService.signUp(name, email, password);
          if (result.session) await applySupabaseSession(result.session);
          else set({authStatus: 'signed-out', user: null});
          return {emailConfirmationRequired: result.session === null};
        } catch (error) {
          set({authStatus: 'error', authError: errorMessage(error), user: null});
          throw error;
        }
      },
      sendPasswordReset: async email => {
        set({authError: null});
        if (!isSupabaseConfigured) return;
        try {
          await supabaseAuthService.sendPasswordReset(email);
        } catch (error) {
          set({authError: errorMessage(error)});
          throw error;
        }
      },
      logout: async () => {
        realtimeService.disconnect();
        if (isSupabaseConfigured) await supabaseAuthService.signOut();
        set({
          authStatus: 'signed-out', authError: null, user: null,
          onboarding: emptyOnboarding, sessions: [], questionnaireSubmissions: [],
          activeSession: null, sessionStatus: 'ready', dataSyncStatus: 'idle',
          dataSyncError: null, lastSyncedAt: null,
        });
      },
      syncNow: async () => {
        const state = get();
        if (!isSupabaseConfigured || !state.user) return;
        set({dataSyncStatus: 'syncing', dataSyncError: null});
        try {
          await mindSyncRepository.saveSessions(state.user.id, state.sessions);
          await mindSyncRepository.savePendingQuestionnaires(state.questionnaireSubmissions);
          await syncCompleteSessionRecords(state.user.id);
          const account = await mindSyncRepository.loadAccount({id: state.user.id, email: state.user.email, user_metadata: {display_name: state.user.name}});
          set({
            user: account.profile,
            onboarding: account.onboarding ?? {...emptyOnboarding, name: account.profile.name},
            sessions: account.sessions,
            questionnaireSubmissions: account.questionnaireSubmissions,
            dataSyncStatus: 'synced', dataSyncError: null, lastSyncedAt: new Date().toISOString(),
          });
        } catch (error) {
          set({dataSyncStatus: 'offline', dataSyncError: errorMessage(error)});
          throw error;
        }
      },
      updateOnboarding: patch => set(state => ({onboarding: {...state.onboarding, ...patch}})),
      completeOnboarding: async () => {
        const state = get();
        if (!state.user) throw new Error('Sign in before completing onboarding');
        const localUser = {...state.user, name: state.onboarding.name || state.user.name, onboardingComplete: true};
        set({user: localUser, dataSyncError: null});
        if (!isSupabaseConfigured) return;
        set({dataSyncStatus: 'syncing'});
        try {
          const savedUser = await mindSyncRepository.saveOnboarding(state.user.id, state.onboarding);
          set({user: savedUser, dataSyncStatus: 'synced', lastSyncedAt: new Date().toISOString()});
        } catch (error) {
          set({dataSyncStatus: 'offline', dataSyncError: errorMessage(error)});
          throw error;
        }
      },
      scanWearables: async () => { await wearableBleService.scan(); },
      connectWearable: async device => {
        set({selectedWearable: device});
        try {
          await wearableBleService.connect(device.id);
          const user = get().user;
          if (isSupabaseConfigured && user) {
            void mindSyncRepository.registerWearable(user.id, {...device, name: WEARABLE_DEVICE_NAME, verified: true})
              .catch(error => set({dataSyncStatus: 'offline', dataSyncError: errorMessage(error)}));
          }
        } catch { /* state is reported by callbacks */ }
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
            onMessage: message => {
              if (message.messageType === 'quest_state') {
                const phase = typeof message.payload.phase === 'string' ? message.payload.phase : null;
                const terminal = phase === 'completed' || phase === 'aborted';
                set(current => ({
                  relay: {...current.relay, questPhase: phase},
                  vrStatus: phase === 'adaptive' ? 'active' : 'ready',
                  sessionStatus: terminal ? 'complete' : current.sessionStatus,
                }));
                if (terminal) get().refreshVisualLog().catch(() => undefined);
                return;
              }
              if (message.messageType === 'visual_telemetry_batch') {
                set(current => ({relay: {
                  ...current.relay,
                  visualTelemetryMessages: [...current.relay.visualTelemetryMessages, message].slice(-200),
                }}));
              }
            },
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
        if (!prepared) return null;
        set(state => ({relay: {
          ...state.relay,
          visualLogDeliveryStatus: 'downloading',
          lastError: null,
        }}));
        try {
          const snapshot = await realtimeService.fetchVisualLog(prepared);
          if (!snapshot.finalized || !snapshot.lastMessageId) {
            set(state => ({relay: {
              ...state.relay,
              visualTelemetryMessages: snapshot.messages,
              visualLogSnapshot: snapshot,
              visualLogDeliveryStatus: 'pending',
              visualLogMessageCount: snapshot.messageCount,
              lastError: null,
            }}));
            return snapshot;
          }
          if (!snapshot.deliveryAcknowledged) {
            await realtimeService.acknowledgeVisualLog(prepared, snapshot);
          }
          const acknowledgedSnapshot: VisualLogSnapshot = {
            ...snapshot,
            deliveryAcknowledged: true,
          };
          set(state => ({relay: {
            ...state.relay,
            visualTelemetryMessages: snapshot.messages,
            visualLogSnapshot: acknowledgedSnapshot,
            visualLogDeliveryStatus: 'acknowledged',
            visualLogMessageCount: snapshot.messageCount,
            lastError: null,
          }}));
          return acknowledgedSnapshot;
        } catch (error) {
          set(state => ({relay: {
            ...state.relay,
            visualLogDeliveryStatus: 'error',
            lastError: error instanceof Error ? error.message : 'relay-log-download-failed',
          }}));
          throw error;
        }
      },
      createSession: () => {
        const session: MeditationSession = {
          id: `session-${Date.now()}`, title: 'Japanese Temple Pond Garden', date: new Date().toISOString().slice(0, 10),
          durationMinutes: 20, environment: 'Temple Pond', audioProfile: 'Adaptive audio',
          completionRate: 0, moodBefore: 5, moodAfter: 0, validationComplete: false,
        };
        set({activeSession: session, sessionStatus: 'ready', sessions: [session, ...get().sessions.filter(item => item.id !== session.id)]});
        const user = get().user;
        if (isSupabaseConfigured && user) {
          void mindSyncRepository.saveSession(user.id, session)
            .catch(error => set({dataSyncStatus: 'offline', dataSyncError: errorMessage(error)}));
        }
        return session;
      },
      setSessionStatus: status => {
        set({sessionStatus: status});
        const state = get();
        if (isSupabaseConfigured && state.user && state.activeSession) {
          const databaseStatus = status === 'complete' ? 'complete' : status;
          void mindSyncRepository.saveSession(state.user.id, state.activeSession, databaseStatus)
            .catch(error => set({dataSyncStatus: 'offline', dataSyncError: errorMessage(error)}));
        }
      },
      submitQuestionnaire: async (templateId, sessionId, answers) => {
        const submission: QuestionnaireSubmission = {
          id: `response-${Date.now()}`, templateId, sessionId, userId: get().user?.id ?? 'anonymous',
          submittedAt: new Date().toISOString(), synced: false, exportShapeVersion: 'component-d-v1', answers,
        };
        set(state => ({questionnaireSubmissions: [submission, ...state.questionnaireSubmissions], pendingValidationCount: Math.max(0, state.pendingValidationCount - 1)}));
        if (!isSupabaseConfigured || submission.userId === 'anonymous') return;
        set({dataSyncStatus: 'syncing', dataSyncError: null});
        try {
          const linkedSession = sessionId ? get().sessions.find(item => item.id === sessionId) : null;
          if (linkedSession) await mindSyncRepository.saveSession(submission.userId, linkedSession);
          await mindSyncRepository.saveQuestionnaire(submission);
          set(state => ({
            questionnaireSubmissions: state.questionnaireSubmissions.map(item => item.id === submission.id ? {...item, synced: true} : item),
            dataSyncStatus: 'synced', lastSyncedAt: new Date().toISOString(),
          }));
        } catch (error) {
          set({dataSyncStatus: 'offline', dataSyncError: errorMessage(error)});
        }
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
      resetDemo: () => {
        if (isSupabaseConfigured) return;
        realtimeService.disconnect();
        set({authStatus: 'authenticated', user: demoUser, onboarding: {...emptyOnboarding, name: demoUser.name, consentAccepted: true, researchConsent: true}, sessions: demoSessions, questionnaireSubmissions: [], pendingValidationCount: 1, vrStatus: 'not-paired', pairingCode: null, activeSession: null, voice: emptyVoice, relay: emptyRelay});
      },
    }),
    {
      name: 'mindsync-rn-state-v1',
      version: 2,
      storage: createJSONStorage(() => AsyncStorage),
      partialize: state => ({user: state.user, onboarding: state.onboarding, sessions: state.sessions, questionnaireSubmissions: state.questionnaireSubmissions, pendingValidationCount: state.pendingValidationCount, componentB: {...emptyComponentB, endpoint: state.componentB.endpoint}}),
      onRehydrateStorage: () => state => {
        state?.setHydrated(true);
        componentBPipelineService.setEndpoint(state?.componentB.endpoint ?? environment.componentBIngestUrl);
      },
    },
  ),
);

async function applySupabaseSession(session: Session | null): Promise<void> {
  if (!session) {
    useMindSyncStore.setState({authStatus: 'signed-out', authError: null, user: null});
    return;
  }
  useMindSyncStore.setState({authStatus: 'authenticating', authError: null, dataSyncStatus: 'syncing'});
  const current = useMindSyncStore.getState();
  try {
    const pending = current.questionnaireSubmissions.filter(item => !item.synced && item.userId === session.user.id);
    if (current.user?.id === session.user.id) {
      await mindSyncRepository.saveSessions(session.user.id, current.sessions);
    }
    if (pending.length) await mindSyncRepository.savePendingQuestionnaires(pending);
    await syncCompleteSessionRecords(session.user.id);
    const account = await mindSyncRepository.loadAccount(session.user);
    useMindSyncStore.setState({
      authStatus: 'authenticated', authError: null,
      user: account.profile,
      onboarding: account.onboarding ?? {...emptyOnboarding, name: account.profile.name},
      sessions: account.sessions,
      questionnaireSubmissions: account.questionnaireSubmissions,
      pendingValidationCount: 0,
      dataSyncStatus: 'synced', dataSyncError: null, lastSyncedAt: new Date().toISOString(),
    });
  } catch (error) {
    const fallbackName = typeof session.user.user_metadata?.display_name === 'string'
      ? session.user.user_metadata.display_name
      : session.user.email?.split('@')[0] ?? 'Participant';
    useMindSyncStore.setState({
      authStatus: 'authenticated',
      user: {
        id: session.user.id, email: session.user.email ?? '', name: fallbackName,
        role: 'participant', onboardingComplete: current.user?.onboardingComplete ?? false,
        preferredLanguage: current.user?.preferredLanguage ?? 'English',
      },
      dataSyncStatus: 'offline', dataSyncError: errorMessage(error),
    });
  }
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected Supabase error';
}

async function syncCompleteSessionRecords(userId: string): Promise<void> {
  const entries = await sessionRecordOutbox.list();
  for (const entry of entries) {
    try {
      await mindSyncRepository.saveCompleteSessionRecord(userId, entry.record);
      await sessionRecordOutbox.markUploaded(entry.recordId);
    } catch (error) {
      await sessionRecordOutbox.markFailed(entry.recordId, errorMessage(error));
      throw error;
    }
  }
}

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
