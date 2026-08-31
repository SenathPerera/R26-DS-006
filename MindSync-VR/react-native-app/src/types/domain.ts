export type UserRole = 'participant' | 'clinician' | 'researcher';
export type ConnectionState = 'idle' | 'scanning' | 'connecting' | 'connected' | 'disconnected' | 'error';
export type VrStatus = 'not-paired' | 'pairing' | 'ready' | 'waiting' | 'active' | 'disconnected';
export type SessionStatus = 'ready' | 'active' | 'paused' | 'ending' | 'complete';
export type SessionRelayConnectionState = 'idle' | 'connecting' | 'connected' | 'error';
export type VisualLogDeliveryStatus = 'idle' | 'downloading' | 'pending' | 'acknowledged' | 'error';
export type QuestionType = 'single' | 'multiple' | 'likert' | 'text' | 'numeric' | 'slider' | 'voice';

export interface UserProfile {
  id: string;
  email: string;
  name: string;
  role: UserRole;
  onboardingComplete: boolean;
  preferredLanguage: string;
}

export interface OnboardingProfile {
  name: string;
  ageRange: string;
  meditationExperience: string;
  preferredDuration: number;
  goals: string[];
  meditationStyle: string;
  audioPreferences: string[];
  environmentPreferences: string[];
  sensitivities: string[];
  consentAccepted: boolean;
  researchConsent: boolean;
}

export interface WearableDevice {
  id: string;
  name: string;
  rssi: number;
  verified: boolean;
  firmware?: string;
}

export interface WearableTelemetry {
  timestampMs: number | null;
  ir: number | null;
  red: number | null;
  heartRateBpm: number | null;
  rrIntervalMs: number | null;
  spo2: number | null;
  noiseAverage: number | null;
  noisePeak: number | null;
  temperatureC: number | null;
  batteryPercent: number | null;
  statusFlags: number;
  receivedAt: number;
}

export interface RawPpgSample {
  timestampMs: number;
  irValue: number;
}

export interface RawPpgBatch {
  samples: RawPpgSample[];
  receivedAtMs: number;
}

export interface BleIngestionState {
  isStreaming: boolean;
  telemetry: WearableTelemetry | null;
  telemetryCount: number;
  lastError: string | null;
  logs: string[];
}

export type ComponentBConnectionState = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'error';

export interface ComponentBFrame {
  timestamp: number;
  sample_rate: 64.0;
  ppg: number[];
  temperature: number | null;
}

export interface ComponentBPipelineState {
  endpoint: string;
  connectionState: ComponentBConnectionState;
  rawCharacteristicAvailable: boolean;
  rawSamplesReceived: number;
  frameSamplesBuffered: number;
  framesQueued: number;
  framesSent: number;
  framesAcknowledged: number;
  lastFrameTimestamp: number | null;
  lastBackendMessage: string | null;
  lastError: string | null;
  logs: string[];
}

export interface MeditationSession {
  id: string;
  title: string;
  date: string;
  durationMinutes: number;
  environment: string;
  audioProfile: string;
  completionRate: number;
  moodBefore: number;
  moodAfter: number;
  validationComplete: boolean;
}

export interface PreferredEnvironment {
  illumination: number;
  warmth: number;
  atmosphericSoftness: number;
  colorRichness: number;
  ambientMotion: number;
}

export interface PreparedVrSession {
  schemaVersion: string;
  sessionId: string;
  pairingCode: string;
  expiresAt: number;
  mobileToken: string;
}

export interface SessionRelayState {
  connectionState: SessionRelayConnectionState;
  preparedRequestId: string | null;
  preparedSession: PreparedVrSession | null;
  questPhase: string | null;
  visualTelemetryMessages: unknown[];
  visualLogDeliveryStatus: VisualLogDeliveryStatus;
  visualLogMessageCount: number;
  lastError: string | null;
}

export interface BranchRule {
  whenQuestionId: string;
  equals?: string;
  includes?: string;
}

export interface QuestionnaireQuestion {
  id: string;
  prompt: string;
  type: QuestionType;
  required?: boolean;
  helperText?: string;
  options?: string[];
  min?: number;
  max?: number;
  branch?: BranchRule;
}

export interface QuestionnaireTemplate {
  id: string;
  title: string;
  description: string;
  component: string;
  version: string;
  questions: QuestionnaireQuestion[];
}

export interface QuestionnaireSubmission {
  id: string;
  templateId: string;
  sessionId: string | null;
  userId: string;
  submittedAt: string;
  synced: boolean;
  exportShapeVersion: 'component-d-v1';
  answers: Record<string, string | string[] | number>;
}

export interface VoiceCheckInState {
  stage: 'intro' | 'environment' | 'pre' | 'vr' | 'post' | 'report';
  backendHealthy: boolean | null;
  personName: string;
  language: 'english' | 'sinhala';
  sessionId: string | null;
  busy: boolean;
  error: string | null;
}
