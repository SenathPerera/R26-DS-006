// Local session history for the voice check-in — the RN equivalent of the web
// client's localStorage store. Persists completed sessions so a participant can
// be tracked across repeated takes during live testing.

import AsyncStorage from '@react-native-async-storage/async-storage';
import type {FullSessionResult, StressResult} from '../../services/api/componentDService';

const KEY = 'cognivoice_sessions';

export interface SavedVoiceSession {
  id: string;
  at: number;
  participant: string;
  language: string;
  selfPre?: number;
  selfPost?: number;
  pre: StressResult;
  post: StressResult;
  full: FullSessionResult;
}

export async function loadSessions(): Promise<SavedVoiceSession[]> {
  try {
    const raw = await AsyncStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as SavedVoiceSession[]) : [];
  } catch {
    return [];
  }
}

export async function saveSession(entry: SavedVoiceSession): Promise<SavedVoiceSession[]> {
  const existing = await loadSessions();
  const next = [entry, ...existing.filter(s => s.id !== entry.id)].slice(0, 50);
  try {
    await AsyncStorage.setItem(KEY, JSON.stringify(next));
  } catch {
    /* non-fatal — history is a convenience, not the primary record */
  }
  return next;
}

/** Attach a self-reported stress rating (0–10) to a saved session, so the
 *  Validate tab can compare it against the voice reading (validation surface). */
export async function setSelfReport(id: string, selfPost: number): Promise<SavedVoiceSession[]> {
  const existing = await loadSessions();
  const next = existing.map(s => (s.id === id ? {...s, selfPost} : s));
  try {
    await AsyncStorage.setItem(KEY, JSON.stringify(next));
  } catch {
    /* non-fatal */
  }
  return next;
}

export async function clearSessions(): Promise<void> {
  try {
    await AsyncStorage.removeItem(KEY);
  } catch {
    /* ignore */
  }
}
