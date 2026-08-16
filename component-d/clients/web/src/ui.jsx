// Presentational components — the visual language of the app. Screens in
// steps.jsx compose these. Every reading shows the FULL set of scores.

import { useState } from "react";
import { useRecorder, useSpeechRecognition, blobToFile } from "./media.js";

/* ---------------- icons ---------------- */
export const IconMic = (p) => (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" {...p}>
    <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z" /><path d="M5 11a7 7 0 0 0 14 0M12 18v3" />
  </svg>
);
export const IconUpload = (p) => (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}>
    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v13" />
  </svg>
);
const IconSun = () => (<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></svg>);
const IconMoon = () => (<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" /></svg>);
const IconChevron = () => (<svg className="chev" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M6 9l6 6 6-6" /></svg>);

/* ---------------- top bar ---------------- */
export function TopBar({ health, theme, onToggleTheme }) {
  const modelUp = health?.layers?.layer2_fusion;
  const voice = modelUp ? { cls: "live", k: "emotion2vec" } : { cls: "warn", k: health ? "loading" : "offline" };
  return (
    <header className="topbar">
      <div className="brand">
        <div className="mark" aria-hidden="true" />
        <div><h1>CogniVoice</h1><div className="sub">Voice stress check-in · Component D</div></div>
      </div>
      <div className="chips">
        <span className="chip"><span className={`dot ${voice.cls}`} />Voice model <span className="k">{voice.k}</span></span>
        <span className="chip"><span className="dot warn" />Heart rate <span className="k">simulated</span></span>
        <button className="iconbtn" onClick={onToggleTheme} aria-label="Toggle theme">{theme === "dark" ? <IconSun /> : <IconMoon />}</button>
      </div>
    </header>
  );
}

/* ---------------- journey rail ---------------- */
export function Rail({ steps, current, onGo, reachable }) {
  return (
    <nav className="rail" aria-label="Session steps">
      <h2>This session</h2>
      {steps.map((s) => {
        const inPanel = s.group === "panel";
        const disabled = !reachable(s.id);
        const cls = ["step", current === s.id ? "active" : "", s.done ? "done" : ""].filter(Boolean).join(" ");
        return (
          <div key={s.id}>
            {s.sep && <div className="rail-sep">{s.sep}</div>}
            <button className={cls} data-go={s.id} disabled={disabled}
              aria-current={current === s.id ? "step" : undefined} onClick={() => onGo(s.id)}>
              <span className={`idx${inPanel ? " mark" : ""}`}>{s.done ? "✓" : s.mark}</span>
              <span><span className="t">{s.label}</span><span className="d">{s.desc}</span></span>
            </button>
          </div>
        );
      })}
    </nav>
  );
}

/* ---------------- record OR upload ---------------- */
export function AudioInput({ withTranscript, duration = 30, busy, onSubmit, ctaLabel = "Analyse audio" }) {
  const rec = useRecorder();
  const stt = useSpeechRecognition();
  const [mode, setMode] = useState("record");
  const [file, setFile] = useState(null);
  const [transcript, setTranscript] = useState("");

  const selectedFile = mode === "upload"
    ? file
    : (rec.blob ? blobToFile(rec.blob, `rec_${Date.now()}.wav`) : null);

  const toggle = () => {
    if (rec.isRecording) { rec.stop(); if (stt.supported) stt.stop(); }
    else { setTranscript(""); rec.start({ autoStopSeconds: duration }); if (withTranscript && stt.supported) stt.start((t) => setTranscript(t)); }
  };
  const submit = () => { if (selectedFile) onSubmit(selectedFile, transcript.trim()); };

  return (
    <>
      <div className="tabs" role="tablist">
        <button className={`tab${mode === "record" ? " on" : ""}`} onClick={() => setMode("record")}><IconMic width="15" height="15" /> Record live</button>
        <button className={`tab${mode === "upload" ? " on" : ""}`} onClick={() => setMode("upload")}><IconUpload width="15" height="15" /> Upload file</button>
      </div>

      {mode === "record" ? (
        <div className="inputrow">
          <button className={`mic${rec.isRecording ? " recording" : ""}`} onClick={toggle} disabled={busy}>
            <span className="ico"><IconMic /></span>
            {rec.isRecording ? `Stop (${rec.elapsed}s)` : rec.blob ? "Record again" : `Hold to speak`}
          </button>
          <div className="wave" aria-hidden="true">{rec.levels.map((v, i) => <i key={i} style={{ height: `${6 + v * 26}px` }} />)}</div>
          {rec.isRecording ? <span className="timer">{rec.elapsed}s / {duration}s</span>
            : rec.blob ? <span className="timer">recording ready ✓</span> : <span className="or">up to {duration}s</span>}
        </div>
      ) : (
        <>
          <label className="dropzone">
            <IconUpload width="26" height="26" />
            <span className="dz-t">Choose an audio file</span>
            <span className="dz-s">WAV, MP3, M4A, WebM, OGG, FLAC — decoded server-side</span>
            <input type="file" accept="audio/*" hidden onChange={(e) => setFile(e.target.files?.[0] || null)} />
          </label>
          {file && <div className="filechip">✓ {file.name}</div>}
        </>
      )}
      {rec.error && <div className="banner bad">{rec.error}</div>}

      {withTranscript && selectedFile && (
        <textarea className="speechbox" value={transcript} onChange={(e) => setTranscript(e.target.value)}
          placeholder={mode === "record" && stt.supported
            ? "What you said appears here — edit it, or type instead. (Optional, kept with your session.)"
            : "Optional: type what was said, kept with your session record."} />
      )}

      <div className="row" style={{ marginTop: 16 }}>
        <button className="btn primary" onClick={submit} disabled={busy || !selectedFile}>
          {busy ? <><span className="spin" /> Analysing…</> : ctaLabel}
        </button>
        {!selectedFile && <span className="or">{mode === "record" ? "Record first, then analyse." : "Choose a file, then analyse."}</span>}
      </div>
    </>
  );
}

/* ---------------- stress result (ALL scores) ---------------- */
const LEVEL_FILL = { no: "var(--s-none)", mild: "var(--s-mild)", moderate: "var(--s-mod)", high: "var(--s-high)" };
// Mirror the backend's Layer-4 gate (config.CONF_MIN): below this |valence|-based
// confidence, the voice reading is treated as uncertain and DEFERRED to heart rate,
// so a low score here means "can't tell" rather than "confidently calm".
const LOW_CONF = 0.4;

function classifyReasons(r) {
  const out = [];
  out.push(`Valence ${r.valence >= 0 ? "+" : ""}${r.valence.toFixed(2)} — the tone reads as ${r.valence < -0.15 ? "unpleasant" : r.valence > 0.15 ? "pleasant" : "neutral"}.`);
  out.push(`Arousal ${r.arousal >= 0 ? "+" : ""}${r.arousal.toFixed(2)} — energy is ${r.arousal >= 0.15 ? "activated / keyed-up" : r.arousal <= -0.15 ? "subdued / low" : "level"}.`);
  if (r.stress_type === "shutdown") out.push('Negative tone with LOW energy → the withdrawn "freeze" form of stress (captured, not collapsed).');
  else if (r.stress_type === "activated") out.push('Negative tone with HIGH energy → the agitated "fight-or-flight" form of stress.');
  out.push(`Confidence ${r.confidence.toFixed(2)} — ${r.confidence >= 0.7 ? "a clear, well-separated reading" : r.confidence >= 0.4 ? "a moderate reading" : "a faint reading near neutral, treat with care"}.`);
  return out;
}

export function StressCard({ result, showReasons = true }) {
  if (!result) return null;
  const { stress_score, stress_level, stress_type, valence, arousal, confidence, quality } = result;
  const pct = Math.max(3, Math.min(100, stress_score * 10));
  const q = quality || {};
  const lowConf = confidence < LOW_CONF;
  return (
    <div className="score-card">
      <div className="score-top">
        <span className="score-num" style={{ color: lowConf ? "var(--ink-faint)" : LEVEL_FILL[stress_level] }}>{stress_score.toFixed(2)}</span>
        <span className="score-den">/ 10</span>
        <span className={`lvl ${lowConf ? "uncertain" : stress_level}`}>{lowConf ? "uncertain" : stress_level}{stress_type && !lowConf ? ` · ${stress_type}` : ""}</span>
      </div>
      <div className="meter"><i style={{ width: `${pct}%`, background: lowConf ? "var(--ink-faint)" : LEVEL_FILL[stress_level] }} /></div>
      {lowConf && (
        <div className="banner" style={{ marginTop: 12, background: "var(--inset)", border: "1px solid var(--line)", color: "var(--ink)" }}>
          <div><b>Low-confidence voice reading (≈{confidence.toFixed(2)}).</b> This voice sits near neutral, so the score is uncertain — <b>not</b> a confident calm. The system treats voice as unreliable here and <b>defers to the heart-rate signal</b> (Layer 4). This is the expected behaviour for out-of-distribution voices such as Sinhala.</div>
        </div>
      )}
      <div className="metrics-grid">
        <div className="mcell"><div className="mk">Valence</div><div className="mv">{valence >= 0 ? "+" : ""}{valence.toFixed(3)}</div></div>
        <div className="mcell"><div className="mk">Arousal</div><div className="mv">{arousal >= 0 ? "+" : ""}{arousal.toFixed(3)}</div></div>
        <div className="mcell"><div className="mk">Confidence</div><div className="mv">{confidence.toFixed(3)}</div></div>
        <div className="mcell"><div className="mk">Level</div><div className="mv" style={{ fontSize: 14, textTransform: "capitalize" }}>{stress_level}</div></div>
        <div className="mcell"><div className="mk">Type</div><div className="mv" style={{ fontSize: 14, textTransform: "capitalize" }}>{stress_type || "—"}</div></div>
        {q.duration_sec != null && <div className="mcell"><div className="mk">Duration</div><div className="mv">{q.duration_sec.toFixed(1)}s</div></div>}
        {q.speech_fraction != null && <div className="mcell"><div className="mk">Speech</div><div className="mv">{Math.round(q.speech_fraction * 100)}%</div></div>}
        {q.rms != null && <div className="mcell"><div className="mk">Loudness</div><div className="mv">{q.rms.toFixed(3)}</div></div>}
      </div>
      {showReasons && (
        <div className="reasons">
          <div className="rh">◆ Why this reading</div>
          <ul>{classifyReasons(result).map((t, i) => <li key={i}>{t}</li>)}</ul>
        </div>
      )}
    </div>
  );
}

/* ---------------- ambient quality ---------------- */
export function QualityBar({ ambient }) {
  const m = ambient?.metrics || {};
  // Derive a friendly 0–100 from the two things Layer 1 checks: quietness + no speech.
  const noise = Math.max(0, Math.min(1, (m.rms ?? 0) / 0.02));       // 0.02 rms ≈ noisy
  const speech = Math.max(0, Math.min(1, m.speech_fraction ?? 0));
  const score = Math.round(100 * (1 - 0.6 * noise - 0.4 * speech));
  const val = Math.max(0, Math.min(100, score));
  const fill = val >= 70 ? "var(--good)" : val >= 45 ? "var(--warn)" : "var(--bad)";
  return (
    <div className="qbar-wrap">
      <div className="qbar-top"><span className="muted">Ambient quality</span><b style={{ color: fill }}>{val}/100</b></div>
      <div className="qbar"><i style={{ width: `${val}%`, background: fill }} /></div>
      <div className="metrics-grid">
        <div className="mcell"><div className="mk">Background rms</div><div className="mv">{(m.rms ?? 0).toFixed(4)}</div></div>
        <div className="mcell"><div className="mk">Speech detected</div><div className="mv">{(m.speech_seconds ?? 0).toFixed(1)}s</div></div>
        <div className="mcell"><div className="mk">Clipping</div><div className="mv">{Math.round((m.clip_ratio ?? 0) * 100)}%</div></div>
        <div className="mcell"><div className="mk">Duration</div><div className="mv">{(m.duration_sec ?? 0).toFixed(1)}s</div></div>
      </div>
    </div>
  );
}

/* ---------------- report building blocks ---------------- */
export function BigMetric({ label, value, sub, tone }) {
  return (
    <div className={`bigm${tone ? " " + tone : ""}`}>
      <div className="bk">{label}</div>
      <div className="bv">{value}</div>
      {sub && <div className="bs">{sub}</div>}
    </div>
  );
}
export function InsightCard({ icon, tone = "neutral", title, value, text }) {
  return (
    <div className="insight">
      <div className={`ic ${tone}`}>{icon}</div>
      <div className="it">{title}</div>
      <div className="iv">{value}</div>
      <div className="ix">{text}</div>
    </div>
  );
}
export function TechnicalDetails({ panels }) {
  return (
    <details className="disclosure">
      <summary>
        <div><div className="st">Technical detail — every layer's raw output</div><div className="ss">All scores, for the study record. Hidden by default.</div></div>
        <IconChevron />
      </summary>
      <div className="jsons">
        {panels.map((p) => (
          <div className="jsonp" key={p.title}>
            <h4>{p.title}</h4>
            <pre>{p.data ? JSON.stringify(p.data, null, 2) : "No data"}</pre>
          </div>
        ))}
      </div>
    </details>
  );
}

/* ---------------- valence/arousal circumplex ---------------- */
export function Circumplex({ points = [] }) {
  const S = 80, C = 110;
  const px = (v) => C + Math.max(-1, Math.min(1, v)) * S;
  const py = (a) => C - Math.max(-1, Math.min(1, a)) * S;
  const two = points.length >= 2 ? points : null;
  return (
    <svg viewBox="0 0 220 220" width="100%" style={{ maxWidth: 320, margin: "0 auto" }} role="img" aria-label="Valence and arousal plot">
      <defs><marker id="ah" markerWidth="9" markerHeight="9" refX="6" refY="3" orient="auto"><path d="M0,0 L6,3 L0,6 Z" fill="var(--clay)" /></marker></defs>
      <rect x="20" y="20" width="180" height="180" rx="14" fill="var(--inset)" stroke="var(--line)" />
      <line x1="110" y1="24" x2="110" y2="196" stroke="var(--line)" strokeWidth="1" />
      <line x1="24" y1="110" x2="196" y2="110" stroke="var(--line)" strokeWidth="1" />
      <text x="110" y="15" textAnchor="middle" fontSize="8.5" fill="var(--ink-faint)">activated</text>
      <text x="110" y="211" textAnchor="middle" fontSize="8.5" fill="var(--ink-faint)">calm</text>
      <text x="30" y="113" fontSize="8.5" fill="var(--ink-faint)">unpleasant</text>
      <text x="190" y="113" textAnchor="end" fontSize="8.5" fill="var(--ink-faint)">pleasant</text>
      {two && <line x1={px(two[0].valence)} y1={py(two[0].arousal)} x2={px(two[1].valence)} y2={py(two[1].arousal)} stroke="var(--clay)" strokeWidth="1.6" strokeDasharray="3 3" markerEnd="url(#ah)" opacity="0.8" />}
      {points.map((p, i) => (
        <g key={i}>
          <circle cx={px(p.valence)} cy={py(p.arousal)} r="8" fill={p.color} />
          {i === 0 && <circle cx={px(p.valence)} cy={py(p.arousal)} r="13" fill="none" stroke={p.color} strokeWidth="1.4" opacity="0.4" />}
          <text x={px(p.valence)} y={py(p.arousal) - 13} textAnchor="middle" fontSize="8.5" fontWeight="700" fill={p.color}>{p.label}</text>
        </g>
      ))}
    </svg>
  );
}
