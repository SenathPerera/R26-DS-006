import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  COMPLETE_SESSION_SCHEMA_VERSION,
  type CompleteSessionRecord,
} from './completeSessionRecord';

export const SESSION_RECORD_OUTBOX_KEY =
  'mindsync_complete_session_outbox_v1';

interface KeyValueStorage {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
}

export interface SessionRecordOutboxEntry {
  recordId: string;
  status: 'pending';
  enqueuedAtUnixSeconds: number;
  attemptCount: number;
  lastAttemptAtUnixSeconds: number | null;
  lastError: string | null;
  record: CompleteSessionRecord;
}

export class SessionRecordOutbox {
  constructor(private readonly storage: KeyValueStorage = AsyncStorage) {}

  async list(): Promise<SessionRecordOutboxEntry[]> {
    const raw = await this.storage.getItem(SESSION_RECORD_OUTBOX_KEY);
    if (!raw) return [];

    try {
      const parsed = JSON.parse(raw) as unknown;
      return Array.isArray(parsed)
        ? parsed.filter(isSessionRecordOutboxEntry)
        : [];
    } catch {
      return [];
    }
  }

  async enqueue(
    record: CompleteSessionRecord,
    enqueuedAtUnixSeconds = Date.now() / 1000,
  ): Promise<SessionRecordOutboxEntry[]> {
    const entries = await this.list();
    const existing = entries.find(entry => entry.recordId === record.recordId);
    const nextEntry: SessionRecordOutboxEntry = existing
      ? {...existing, record}
      : {
          recordId: record.recordId,
          status: 'pending',
          enqueuedAtUnixSeconds,
          attemptCount: 0,
          lastAttemptAtUnixSeconds: null,
          lastError: null,
          record,
        };
    const next = [
      nextEntry,
      ...entries.filter(entry => entry.recordId !== record.recordId),
    ];
    await this.save(next);
    return next;
  }

  async markFailed(
    recordId: string,
    error: string,
    attemptedAtUnixSeconds = Date.now() / 1000,
  ): Promise<SessionRecordOutboxEntry[]> {
    const entries = await this.list();
    const next = entries.map(entry => entry.recordId === recordId
      ? {
          ...entry,
          attemptCount: entry.attemptCount + 1,
          lastAttemptAtUnixSeconds: attemptedAtUnixSeconds,
          lastError: error || 'session-record-upload-failed',
        }
      : entry);
    await this.save(next);
    return next;
  }

  async markUploaded(recordId: string): Promise<SessionRecordOutboxEntry[]> {
    const entries = await this.list();
    const next = entries.filter(entry => entry.recordId !== recordId);
    await this.save(next);
    return next;
  }

  private save(entries: SessionRecordOutboxEntry[]): Promise<void> {
    return this.storage.setItem(
      SESSION_RECORD_OUTBOX_KEY,
      JSON.stringify(entries),
    );
  }
}

function isSessionRecordOutboxEntry(
  value: unknown,
): value is SessionRecordOutboxEntry {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }
  const candidate = value as Partial<SessionRecordOutboxEntry>;
  return typeof candidate.recordId === 'string'
    && candidate.status === 'pending'
    && typeof candidate.enqueuedAtUnixSeconds === 'number'
    && typeof candidate.attemptCount === 'number'
    && (candidate.lastAttemptAtUnixSeconds === null
      || typeof candidate.lastAttemptAtUnixSeconds === 'number')
    && (candidate.lastError === null || typeof candidate.lastError === 'string')
    && typeof candidate.record === 'object'
    && candidate.record !== null
    && candidate.record.schemaVersion === COMPLETE_SESSION_SCHEMA_VERSION
    && candidate.record.recordId === candidate.recordId;
}

export const sessionRecordOutbox = new SessionRecordOutbox();
