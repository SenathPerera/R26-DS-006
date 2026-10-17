import {
  isPreparedSession,
  isRelayEnvelope,
  isVisualLogAcknowledgement,
  isVisualLogSnapshot,
  SESSION_RELAY_SCHEMA_VERSION,
  sessionControlFromQuestPhase,
  statusFromQuestPhase,
} from './realtimeService';

describe('session relay validation', () => {
  test('Quest pause stays visible until Quest confirms a resume', () => {
    expect(statusFromQuestPhase('paused', 'active')).toBe('paused');
    expect(statusFromQuestPhase('paused', 'paused')).toBe('paused');
    expect(statusFromQuestPhase('adaptive', 'paused')).toBe('active');
    expect(statusFromQuestPhase('completed', 'paused')).toBe('complete');
  });

  test('voice VR stage offers only commands valid for the Quest phase', () => {
    expect(sessionControlFromQuestPhase('ready')).toBeNull();
    expect(sessionControlFromQuestPhase('acclimatization')).toBeNull();
    expect(sessionControlFromQuestPhase('adaptive')).toBe('pause');
    expect(sessionControlFromQuestPhase('paused')).toBe('resume');
    expect(sessionControlFromQuestPhase('completed')).toBeNull();
    expect(sessionControlFromQuestPhase('aborted')).toBeNull();
  });

  test('accepts a complete prepared session', () => {
    expect(isPreparedSession({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      sessionId: 'session-1',
      pairingCode: '012345',
      expiresAt: 1234.5,
      mobileToken: 'secret-runtime-token',
      studyCondition: 'ContextualBandit',
    })).toBe(true);
  });

  test('rejects an incorrectly shaped access code', () => {
    expect(isPreparedSession({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      sessionId: 'session-1',
      pairingCode: '1234',
      expiresAt: 1234.5,
      mobileToken: 'token',
      studyCondition: 'ContextualBandit',
    })).toBe(false);
  });

  test('rejects a prepared session without an approved study condition', () => {
    expect(isPreparedSession({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      sessionId: 'session-1',
      pairingCode: '012345',
      expiresAt: 1234.5,
      mobileToken: 'token',
      studyCondition: 'ExperimentalPolicy',
    })).toBe(false);
  });

  test('accepts only the configured relay envelope schema', () => {
    expect(isRelayEnvelope({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      messageId: 'message-1',
      messageType: 'quest_state',
      payload: {sessionId: 'session-1'},
    })).toBe(true);
    expect(isRelayEnvelope({
      schemaVersion: 'wrong-schema',
      messageId: 'message-1',
      messageType: 'quest_state',
      payload: {},
    })).toBe(false);
  });

  test('validates finalized visual-log snapshots and acknowledgements', () => {
    const terminalMessage = {
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      messageId: 'terminal-message',
      messageType: 'quest_state',
      payload: {sessionId: 'session-1', phase: 'completed'},
    };
    expect(isVisualLogSnapshot({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      sessionId: 'session-1',
      finalized: true,
      completionPhase: 'completed',
      deliveryAcknowledged: false,
      messageCount: 1,
      lastMessageId: 'terminal-message',
      messages: [terminalMessage],
    })).toBe(true);
    expect(isVisualLogSnapshot({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      sessionId: 'session-1',
      finalized: true,
      completionPhase: 'completed',
      deliveryAcknowledged: false,
      messageCount: 2,
      lastMessageId: 'wrong-message',
      messages: [terminalMessage],
    })).toBe(false);
    expect(isVisualLogAcknowledgement({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      sessionId: 'session-1',
      acknowledged: true,
      messageCount: 1,
      lastMessageId: 'terminal-message',
    })).toBe(true);
  });
});
