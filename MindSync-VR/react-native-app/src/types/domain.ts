export type UserRole = 'participant' | 'clinician' | 'researcher';
export type ConnectionState = 'idle' | 'scanning' | 'connecting' | 'connected' | 'disconnected' | 'error';
export type VrStatus = 'not-paired' | 'pairing' | 'ready' | 'waiting' | 'active' | 'disconnected';
export type SessionStatus = 'ready' | 'active' | 'paused' | 'ending' | 'complete';
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

export interface BleIngestionState {
  isStreaming: boolean;
  telemetry: WearableTelemetry | null;
  telemetryCount: number;
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
