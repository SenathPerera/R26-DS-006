import type {
  FullSessionResult,
  StressResult,
} from '../api/componentDService';
import type {VisualLogSnapshot} from '../realtime/realtimeService';
import {
  COMPLETE_SESSION_SCHEMA_VERSION,
  createCompleteSessionRecord,
} from './completeSessionRecord';

describe('createCompleteSessionRecord', () => {
  test('keeps voice and finalized visual contributions namespaced', () => {
    const record = createCompleteSessionRecord({
      sessionId: 'voice-42',
      participantPseudonym: 'participant-7',
      startedAtUnixSeconds: 100,
      completedAtUnixSeconds: 1300,
      language: 'english',
      pre: stress(7),
      post: stress(3),
      voiceSummary: summary(),
      relaySessionId: 'session-relay-9',
      sceneId: 'temple-pond',
      visualLog: visualLog(),
    });

    expect(record.schemaVersion).toBe(COMPLETE_SESSION_SCHEMA_VERSION);
    expect(record.recordId).toBe('voice-42');
    expect(record.voice.componentSessionId).toBe('voice-42');
    expect(record.visual.relaySessionId).toBe('session-relay-9');
    expect(record.visual.messageCount).toBe(1);
    expect(record.audio).toBeNull();
  });

  test('rejects visual logs that are not finalized and acknowledged', () => {
    expect(() => createCompleteSessionRecord({
      sessionId: 'voice-42',
      participantPseudonym: 'participant-7',
      startedAtUnixSeconds: 100,
      completedAtUnixSeconds: 1300,
      language: 'english',
      pre: stress(7),
      post: stress(3),
      voiceSummary: summary(),
      relaySessionId: 'session-relay-9',
      sceneId: 'temple-pond',
      visualLog: {...visualLog(), deliveryAcknowledged: false},
    })).toThrow('complete-session-visual-log-not-ready');
  });

  test('rejects a visual log from another relay session', () => {
    expect(() => createCompleteSessionRecord({
      sessionId: 'voice-42',
      participantPseudonym: 'participant-7',
      startedAtUnixSeconds: 100,
      completedAtUnixSeconds: 1300,
      language: 'english',
      pre: stress(7),
      post: stress(3),
      voiceSummary: summary(),
      relaySessionId: 'session-relay-other',
      sceneId: 'temple-pond',
      visualLog: visualLog(),
    })).toThrow('complete-session-relay-id-mismatch');
  });
});

function stress(stressScore: number): StressResult {
  return {
    stress_score: stressScore,
    stress_level: 'moderate',
    confidence: 0.8,
    valence: 0.4,
    arousal: 0.6,
  };
}

function summary(): FullSessionResult {
  return {
    stress_level: 3,
    confidence: 0.8,
    verdict: {
      primary_signal: 'voice',
      session_helped: true,
      direction: 'improved',
      reliable: true,
      note: 'test',
    },
    comparison: {
      direction: 'improved',
      improved: true,
      reliable: true,
      delta: -4,
      pre_stress: 7,
      post_stress: 3,
    },
    crossmodal: null,
    anomaly: null,
    personal_baseline: {personalised: false},
  };
}

function visualLog(): VisualLogSnapshot {
  return {
    schemaVersion: 'mindsync-session-v1',
    sessionId: 'session-relay-9',
    finalized: true,
    completionPhase: 'completed',
    deliveryAcknowledged: true,
    messageCount: 1,
    lastMessageId: 'message-1',
    messages: [{
      schemaVersion: 'mindsync-session-v1',
      messageId: 'message-1',
      messageType: 'quest_state',
      payload: {phase: 'completed'},
    }],
  };
}
