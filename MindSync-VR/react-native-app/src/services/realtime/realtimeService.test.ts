import {
  isPreparedSession,
  isRelayEnvelope,
  isVisualLogAcknowledgement,
  isVisualLogSnapshot,
  SESSION_RELAY_SCHEMA_VERSION,
} from './realtimeService';

describe('session relay validation', () => {
  test('accepts a complete prepared session', () => {
    expect(isPreparedSession({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      sessionId: 'session-1',
      pairingCode: '012345',
      expiresAt: 1234.5,
      mobileToken: 'secret-runtime-token',
    })).toBe(true);
  });

  test('rejects an incorrectly shaped access code', () => {
    expect(isPreparedSession({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      sessionId: 'session-1',
      pairingCode: '1234',
      expiresAt: 1234.5,
      mobileToken: 'token',
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
