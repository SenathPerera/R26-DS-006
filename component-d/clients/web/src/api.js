// API client for Component D. One place that knows the server contract, so
// every screen calls typed helpers instead of raw fetch. See api_server.py.

// Backend base URL. Override without editing code via a Vite env var, e.g.
//   VITE_API_BASE=http://localhost:9001 npm run dev
export const API_BASE = import.meta.env.VITE_API_BASE || "http://localhost:8010";

// Demo switch: when true, D polls the REAL Component B (GET /stress/latest) at
// each /infer and the mock HRV is turned off. Off by default -> simulated HRV,
// so the UI runs with no B connected. Enable with VITE_POLL_B=true npm run dev
// (point D at B via COMPONENT_B_URL on the server side).
export const POLL_B = String(import.meta.env.VITE_POLL_B).toLowerCase() === "true";

function netError() {
  const e = new Error(`Can't reach the server on ${API_BASE} — make sure it's running, then try again.`);
  e.isNetwork = true;
  return e;
}

async function postForm(endpoint, fields = {}, files = {}) {
  const form = new FormData();
  Object.entries(fields).forEach(([k, v]) => form.append(k, v));
  Object.entries(files).forEach(([k, v]) => form.append(k, v));
  let res;
  try { res = await fetch(`${API_BASE}${endpoint}`, { method: "POST", body: form }); }
  catch { throw netError(); }
  if (!res.ok) throw await asError(res);
  return res.json();
}

async function postJson(endpoint, body) {
  let res;
  try {
    res = await fetch(`${API_BASE}${endpoint}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  } catch { throw netError(); }
  if (!res.ok) throw await asError(res);
  return res.json();
}

// The API's Layer-1 rejection (422) sends an OBJECT detail {error, reasons:[]};
// every other error sends a plain string. Turn both into a readable message.
async function asError(res) {
  let msg = `HTTP ${res.status}`;
  try {
    const data = await res.json();
    if (data.detail && typeof data.detail === "object") {
      msg = data.detail.reasons?.join(", ") || data.detail.error || msg;
    } else {
      msg = data.detail || msg;
    }
  } catch { /* body wasn't JSON */ }
  const err = new Error(msg);
  err.status = res.status;
  return err;
}

// --- endpoints -------------------------------------------------------------

export const getHealth = () =>
  fetch(`${API_BASE}/health`).then((r) => {
    if (!r.ok) throw new Error(`health ${r.status}`);
    return r.json();
  });

// Layer 1 — room quality. -> {ok, reasons[], metrics}
export const ambientCheck = (file) => postForm("/ambient-check", {}, { file });

// Layer 2 — voice stress. -> {stress_score, stress_level, stress_type,
//   confidence, valence, arousal, quality, session_id}
// opts: { pollB, log, userId, language } — log=true persists the clip + scores
// (for the live test); userId/language tag the record.
export const infer = (file, sessionId, phase, opts = {}) => {
  const { pollB = false, log = false, userId, language } = opts;
  const q = new URLSearchParams({ session_id: sessionId, phase, poll_b: String(pollB), log: String(log) });
  if (userId) q.set("user_id", userId);
  if (language) q.set("language", language);
  return postForm(`/infer?${q.toString()}`, {}, { file });
};

// LLM companion turn. -> {reply}
export const companionMessage = (sessionId, text, phase) =>
  postJson(`/companion/message?phase=${phase}`, { session_id: sessionId, text });

// Component B contract — push an ordinal HRV stress level for a phase.
export const sessionUpdate = (sessionId, phase, stressLevel) =>
  postJson("/session-update", { session_id: sessionId, phase, stress_level: stressLevel });

// Layers 3+4+5 combined. -> {verdict, comparison, crossmodal, anomaly, personal_baseline, ...}
// opts: { useMockHrv, language, selfPre, selfPost, log } — self-report values are
// the subject's own 0-10 stress (ground truth), written to the log when log=true.
export const fullSession = (sessionId, userId, opts = {}) => {
  const { useMockHrv = false, language, selfPre, selfPost, log = false } = opts;
  const body = { session_id: sessionId, user_id: userId, use_mock_hrv: useMockHrv, log };
  if (language) body.language = language;
  if (selfPre !== undefined && selfPre !== "") body.self_report_pre = Number(selfPre);
  if (selfPost !== undefined && selfPost !== "") body.self_report_post = Number(selfPost);
  return postJson("/full-session", body);
};
