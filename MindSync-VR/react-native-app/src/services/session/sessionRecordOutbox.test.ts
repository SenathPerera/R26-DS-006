jest.mock('@react-native-async-storage/async-storage', () => ({
  __esModule: true,
  default: {
    getItem: jest.fn(),
    setItem: jest.fn(),
  },
}));

import type {CompleteSessionRecord} from './completeSessionRecord';
import {SessionRecordOutbox} from './sessionRecordOutbox';

describe('SessionRecordOutbox', () => {
  test('enqueues idempotently by record id and removes only after upload', async () => {
    const storage = new MemoryStorage();
    const outbox = new SessionRecordOutbox(storage);
    const original = record('voice-1', 3);

    await outbox.enqueue(original, 100);
    await outbox.enqueue({...original, completedAtUnixSeconds: 1400}, 200);

    let entries = await outbox.list();
    expect(entries).toHaveLength(1);
    expect(entries[0].enqueuedAtUnixSeconds).toBe(100);
    expect(entries[0].record.completedAtUnixSeconds).toBe(1400);

    await outbox.markFailed('voice-1', 'network-offline', 1500);
    entries = await outbox.list();
    expect(entries[0].attemptCount).toBe(1);
    expect(entries[0].lastError).toBe('network-offline');

    await outbox.markUploaded('voice-1');
    expect(await outbox.list()).toEqual([]);
  });

  test('ignores corrupt stored entries', async () => {
    const storage = new MemoryStorage();
    await storage.setItem(
      'mindsync_complete_session_outbox_v1',
      JSON.stringify([{recordId: 'broken'}]),
    );

    expect(await new SessionRecordOutbox(storage).list()).toEqual([]);
  });
});

class MemoryStorage {
  private readonly values = new Map<string, string>();

  async getItem(key: string): Promise<string | null> {
    return this.values.get(key) ?? null;
  }

  async setItem(key: string, value: string): Promise<void> {
    this.values.set(key, value);
  }
}

function record(recordId: string, postStress: number): CompleteSessionRecord {
  return {
    schemaVersion: 'mindsync-complete-session-v1',
    recordId,
    sessionId: recordId,
    participantPseudonym: 'participant-7',
    startedAtUnixSeconds: 100,
    completedAtUnixSeconds: 1300,
    voice: {
      componentSessionId: recordId,
      language: 'english',
      pre: {
        stress_score: 7,
        stress_level: 'moderate',
        confidence: 0.8,
        valence: 0.4,
        arousal: 0.6,
      },
      post: {
        stress_score: postStress,
        stress_level: 'mild',
        confidence: 0.8,
        valence: 0.5,
        arousal: 0.3,
      },
      summary: {
        stress_level: postStress,
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
          delta: postStress - 7,
          pre_stress: 7,
          post_stress: postStress,
        },
        crossmodal: null,
        anomaly: null,
        personal_baseline: {personalised: false},
      },
    },
    visual: {
      relaySchemaVersion: 'mindsync-session-v1',
      relaySessionId: 'session-relay-9',
      sceneId: 'temple-pond',
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
    },
    audio: null,
  };
}
