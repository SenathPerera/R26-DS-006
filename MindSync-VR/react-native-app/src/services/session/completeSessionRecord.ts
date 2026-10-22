import type {
  FullSessionResult,
  StressResult,
} from '../api/componentDService';
import type {VisualLogSnapshot} from '../realtime/realtimeService';

export const COMPLETE_SESSION_SCHEMA_VERSION =
  'mindsync-complete-session-v1' as const;

export interface VoiceSessionContribution {
  componentSessionId: string;
  language: string;
  pre: StressResult;
  post: StressResult;
  summary: FullSessionResult;
}

export interface VisualSessionContribution {
  relaySchemaVersion: string;
  relaySessionId: string;
  sceneId: string;
  completionPhase: 'completed' | 'aborted';
  deliveryAcknowledged: true;
  messageCount: number;
  lastMessageId: string;
  messages: VisualLogSnapshot['messages'];
}

export interface CompleteSessionRecord {
  schemaVersion: typeof COMPLETE_SESSION_SCHEMA_VERSION;
  recordId: string;
  sessionId: string;
  participantPseudonym: string;
  startedAtUnixSeconds: number;
  completedAtUnixSeconds: number;
  voice: VoiceSessionContribution;
  visual: VisualSessionContribution;
  audio: null;
}

export interface CreateCompleteSessionRecordInput {
  sessionId: string;
  participantPseudonym: string;
  startedAtUnixSeconds: number;
  completedAtUnixSeconds: number;
  language: string;
  pre: StressResult;
  post: StressResult;
  voiceSummary: FullSessionResult;
  relaySessionId: string;
  sceneId: string;
  visualLog: VisualLogSnapshot;
}

export function createCompleteSessionRecord(
  input: CreateCompleteSessionRecordInput,
): CompleteSessionRecord {
  requireText(input.sessionId, 'sessionId');
  requireText(input.participantPseudonym, 'participantPseudonym');
  requireText(input.language, 'language');
  requireText(input.relaySessionId, 'relaySessionId');
  requireText(input.sceneId, 'sceneId');
  requireTimestamp(input.startedAtUnixSeconds, 'startedAtUnixSeconds');
  requireTimestamp(input.completedAtUnixSeconds, 'completedAtUnixSeconds');

  if (input.completedAtUnixSeconds < input.startedAtUnixSeconds) {
    throw new Error('complete-session-time-range-invalid');
  }
  if (input.visualLog.sessionId !== input.relaySessionId) {
    throw new Error('complete-session-relay-id-mismatch');
  }
  if (
    !input.visualLog.finalized
    || !input.visualLog.deliveryAcknowledged
    || input.visualLog.completionPhase === null
    || input.visualLog.lastMessageId === null
  ) {
    throw new Error('complete-session-visual-log-not-ready');
  }

  return {
    schemaVersion: COMPLETE_SESSION_SCHEMA_VERSION,
    recordId: input.sessionId,
    sessionId: input.sessionId,
    participantPseudonym: input.participantPseudonym,
    startedAtUnixSeconds: input.startedAtUnixSeconds,
    completedAtUnixSeconds: input.completedAtUnixSeconds,
    voice: {
      componentSessionId: input.sessionId,
      language: input.language,
      pre: input.pre,
      post: input.post,
      summary: input.voiceSummary,
    },
    visual: {
      relaySchemaVersion: input.visualLog.schemaVersion,
      relaySessionId: input.relaySessionId,
      sceneId: input.sceneId,
      completionPhase: input.visualLog.completionPhase,
      deliveryAcknowledged: true,
      messageCount: input.visualLog.messageCount,
      lastMessageId: input.visualLog.lastMessageId,
      messages: [...input.visualLog.messages],
    },
    audio: null,
  };
}

function requireText(value: string, fieldName: string): void {
  if (!value.trim()) throw new Error(`complete-session-${fieldName}-required`);
}

function requireTimestamp(value: number, fieldName: string): void {
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`complete-session-${fieldName}-invalid`);
  }
}
