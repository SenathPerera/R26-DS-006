// API client for Component D — the one place that knows the server contract, so
// every screen calls typed helpers instead of raw fetch. Mirrors the proven web
// client (component-d/clients/web/src/api.js) against the same backend
// (component-d/server/main.py is the source of truth for these shapes).
//
// On device, reach the backend over `adb reverse tcp:8010 tcp:8010` so
// localhost:8010 on the phone tunnels to the Mac. If that isn't set, calls fail
// with a network error and the UI shows the offline banner.

import {environment} from '../../config/environment';

const BASE = environment.componentDBaseUrl;

// ---------------------------------------------------------------- error model
// A network failure (server down / adb reverse not set) is distinct from an HTTP
// error the server chose to return — the UI treats the two differently.
export class ComponentDError extends Error {
  status?: number;
  isNetwork: boolean;
  reasons?: string[];
  constructor(message: string, opts: {status?: number; isNetwork?: boolean; reasons?: string[]} = {}) {
    super(message);
    this.name = 'ComponentDError';
    this.status = opts.status;
    this.isNetwork = opts.isNetwork ?? false;
    this.reasons = opts.reasons;
  }
}

function netError() {
  return new ComponentDError(
    `Can't reach Component D on ${BASE}. Start the server, and on device run "adb reverse tcp:8010 tcp:8010" after every reconnect.`,
    {isNetwork: true},
  );
}

// The API's Layer-1 rejection (422) sends an OBJECT detail {error, reasons:[]};
// every other error sends a plain string. Turn both into a readable message.
async function asError(res: Response): Promise<ComponentDError> {
  let msg = `HTTP ${res.status}`;
  let reasons: string[] | undefined;
  try {
    const data = await res.json();
    if (data?.detail && typeof data.detail === 'object') {
      reasons = data.detail.reasons;
      msg = data.detail.reasons?.join(', ') || data.detail.error || msg;
    } else if (data?.detail) {
      msg = data.detail;
    }
  } catch {
    /* body wasn't JSON */
  }
  return new ComponentDError(msg, {status: res.status, reasons});
}

// ---------------------------------------------------------------- transport
// RN FormData takes a file part as {uri, name, type}. Callers pass the WAV file
// URI produced by the native recorder (raw 16-bit PCM mono — matches the backend
// soundfile fast-path and keeps Layer-1 acoustic analysis honest).
export interface AudioPart {
  uri: string;
  name?: string;
  type?: string;
}

function appendAudio(form: FormData, audio: AudioPart) {
  form.append('file', {
    uri: audio.uri,
    name: audio.name ?? 'clip.wav',
    type: audio.type ?? 'audio/wav',
  } as unknown as Blob);
}

async function postForm<T>(path: string, audio: AudioPart): Promise<T> {
  const form = new FormData();
  appendAudio(form, audio);
  let res: Response;
  try {
    res = await fetch(`${BASE}${path}`, {method: 'POST', body: form});
  } catch {
    throw netError();
  }
  if (!res.ok) throw await asError(res);
  return res.json() as Promise<T>;
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${BASE}${path}`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify(body),
    });
  } catch {
    throw netError();
  }
  if (!res.ok) throw await asError(res);
  return res.json() as Promise<T>;
}

function qs(params: Record<string, string | number | boolean | undefined>): string {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v !== undefined && v !== '') q.set(k, String(v));
  });
  const s = q.toString();
  return s ? `?${s}` : '';
}

// ---------------------------------------------------------------- types
// Shapes below track component-d/server/main.py return payloads exactly.

export interface HealthLayers {
  layer1_quality: boolean;
  layer2_fusion: boolean;
  layer3_compare: boolean;
  layer4_crossmodal: boolean;
  layer5_anomaly: boolean;
}
export interface ComponentDHealth {
  status: string;
  layers: HealthLayers;
}

// Layer 1 — /ambient-check
export interface AmbientCheckItem {
  id: string;
  label: string;
  value: number;
  unit: string;
  pass: boolean;
  severity: string;
  message: string;
}
export interface AmbientResult {
  ok: boolean;
  score?: number;
  noise_type?: 'quiet' | 'hum' | 'broadband' | 'hiss' | 'intermittent' | 'voices' | string;
  verdict?: 'good' | 'usable' | 'too_noisy' | 'voices' | 'clipping' | string;
  reasons?: string[];
  checks?: AmbientCheckItem[];
  metrics?: Record<string, number>;
}

// Layer 2 — the per-clip stress reading (from /infer and voice-turn `analysis`).
export interface StressResult {
  stress_score: number;
  stress_level: string;
  stress_type?: string;
  confidence: number;
  valence: number;
  arousal: number;
  quality?: Record<string, number>;
  input_level?: string;
  warnings?: string[];
  reasons?: string[];
  body?: {level: string; confidence: number; source: string} | null;
  session_id?: string;
}

// /companion/voice-turn — one conversational turn (STT + reply + optional score).
export interface VoiceTurnResult {
  transcript: string;
  reply: string;
  crisis: boolean;
  accepted: boolean;
  reasons: string[];
  quality: Record<string, number> | null;
  analysis: StressResult | null;
  session_id: string;
}

// Layer 3 — comparison
export interface Comparison {
  direction: 'improved' | 'worsened' | 'unchanged' | string;
  improved: boolean;
  reliable: boolean;
  delta: number;
  pre_stress: number;
  post_stress: number;
  magnitude?: string;
}

// Layer 4 — cross-modal (voice × heart)
export interface CrossModal {
  validated?: boolean;
  low_confidence?: boolean;
  agreement?: number;
  mismatch_type?: string;
  unresolved_mismatch?: string;
  note?: string;
  voice?: {pre?: number; post?: number; confidence?: {pre?: number; post?: number}};
  body?: {pre?: number; post?: number};
}

// Layer 5 — anomaly
export interface Anomaly {
  anomaly: boolean;
  anomaly_direction?: string;
  severity?: string;
}

export interface PersonalBaseline {
  personalised: boolean;
  relative_band?: string;
}

export interface FullSessionResult {
  stress_level: number;
  confidence: number;
  verdict: {
    primary_signal: string;
    session_helped: boolean;
    direction: string;
    reliable: boolean;
    note: string;
  };
  comparison: Comparison;
  crossmodal: CrossModal | null;
  anomaly: Anomaly | null;
  personal_baseline: PersonalBaseline;
}

export interface SessionSummary {
  session_id: string;
  user_id?: string;
  language?: string;
  created_at?: string;
  [k: string]: unknown;
}

// ---------------------------------------------------------------- endpoints

export interface VoiceTurnOptions {
  phase: 'pre' | 'post';
  userId?: string;
  language?: string;
  pollB?: boolean;
  log?: boolean;
  isFinal?: boolean;
}

export interface FullSessionOptions {
  useMockHrv?: boolean;
  language?: string;
  selfReportPre?: number;
  selfReportPost?: number;
  notes?: string;
  log?: boolean;
}

export const componentDService = {
  async health(): Promise<ComponentDHealth> {
    let res: Response;
    try {
      res = await fetch(`${BASE}/health`);
    } catch {
      throw netError();
    }
    if (!res.ok) throw await asError(res);
    return res.json() as Promise<ComponentDHealth>;
  },

  // Layer 1 — room quality gate. Continue only when ok === true.
  ambientCheck(audio: AudioPart): Promise<AmbientResult> {
    return postForm<AmbientResult>('/ambient-check', audio);
  },

  // Layer 2 + companion — one conversational turn. is_final scores + stores the
  // clip so /full-session works unchanged; non-final turns transcribe + reply only.
  voiceTurn(audio: AudioPart, sessionId: string, opts: VoiceTurnOptions): Promise<VoiceTurnResult> {
    const path = `/companion/voice-turn${qs({
      session_id: sessionId,
      phase: opts.phase,
      user_id: opts.userId,
      language: opts.language,
      poll_b: opts.pollB ?? false,
      log: opts.log ?? false,
      is_final: opts.isFinal ?? false,
    })}`;
    return postForm<VoiceTurnResult>(path, audio);
  },

  // Layers 3+4+5 combined, run once after the post recording.
  fullSession(sessionId: string, userId: string, opts: FullSessionOptions = {}): Promise<FullSessionResult> {
    const body: Record<string, unknown> = {
      session_id: sessionId,
      user_id: userId,
      use_mock_hrv: opts.useMockHrv ?? false,
      log: opts.log ?? false,
    };
    if (opts.language) body.language = opts.language;
    if (opts.selfReportPre !== undefined) body.self_report_pre = opts.selfReportPre;
    if (opts.selfReportPost !== undefined) body.self_report_post = opts.selfReportPost;
    if (opts.notes) body.notes = opts.notes;
    return postJson<FullSessionResult>('/full-session', body);
  },

  // Realistic companion voice (ElevenLabs, proxied server-side). Returns the URL
  // to stream/play; a 503 from here means "no key set" — the app plays on-device
  // TTS instead, so the companion always speaks.
  ttsUrl(text: string, language?: string): string {
    return `${BASE}/companion/tts${qs({text, language})}`;
  },

  // Warm the models (encoder + STT) so the first analysis isn't a cold start.
  // Fire-and-forget on check-in entry; never throws.
  warmup(): Promise<void> {
    return fetch(`${BASE}/warmup`, {method: 'POST'}).then(() => undefined).catch(() => undefined);
  },

  // Session history (read).
  listSessions(userId: string, limit = 20): Promise<SessionSummary[]> {
    return fetch(`${BASE}/sessions${qs({user_id: userId, limit})}`).then(res => {
      if (!res.ok) throw new ComponentDError(`sessions ${res.status}`, {status: res.status});
      return res.json() as Promise<SessionSummary[]>;
    });
  },

  getSession(sessionId: string): Promise<unknown> {
    return fetch(`${BASE}/session/${encodeURIComponent(sessionId)}`).then(res => {
      if (!res.ok) throw new ComponentDError(`session ${res.status}`, {status: res.status});
      return res.json();
    });
  },
};
