// The screens of the session flow. App.jsx owns state + passes data/callbacks.

import { useEffect, useState } from "react";
import { AudioInput, StressCard, QualityBar, Circumplex, BigMetric, InsightCard, TechnicalDetails, stressTypeLabel } from "./ui.jsx";

const PROMPTS = {
  pre: { title: "How are you arriving today?", sub: "Speak for about 30 seconds — tell me about your day. Any deadlines, pressure, or things on your mind? There are no right answers; just talk naturally." },
  post: { title: "How do you feel now?", sub: "Speak for about 30 seconds — what's shifted since before the session, even a little? Lighter, calmer, or still active?" },
};
function mismatchDetail(t) {
  switch (t) {
    case "vocal_masking": return "Your voice sounds calmer than your heart-rate signal — stress may be consciously controlled in speech.";
    case "cognitive_persistence": return "Your body settled, but your voice still carries tension — the mind may still be processing.";
    case "baseline_divergence": return "Voice and heart rate read differently through the session, so the comparison is less certain.";
    case "outcome_divergence": return "Voice and heart rate agreed before, but diverged afterward.";
    default: return "Voice and heart-rate comparison.";
  }
}

// One verdict object for Layer 4, covering the confidence-gated states: agree,
// a genuine mismatch, or "voice uncertain -> deferred to heart rate".
function crossVerdict(cross) {
  if (!cross) return { label: "No heart data", tone: "neutral", icon: "–", short: "No data",
    detail: "Component B didn't report a heart-rate reading for this session." };
  if (cross.low_confidence) return { label: "Voice uncertain — deferred to heart rate", tone: "neutral", icon: "≈", short: "Deferred",
    detail: cross.note || "The voice reading was too faint to confirm a mismatch, so the heart-rate signal is trusted here instead of raising a false flag." };
  if (cross.validated) return { label: "Voice and heart rate agree", tone: "good", icon: "✓", short: "Agree",
    detail: `Both signals read the same direction (agreement ${cross.agreement}).` };
  return { label: `Mismatch — ${(cross.mismatch_type || "").replace(/_/g, " ")}`, tone: "warn", icon: "≠", short: "Differ",
    detail: mismatchDetail(cross.mismatch_type) };
}

// Detailed voice-vs-heart panel, with the confidence Layer 4 actually used.
function CrossModalCard({ cross }) {
  if (!cross) return null;
  const v = crossVerdict(cross);
  const vc = cross.voice?.confidence || {};
  const bannerCls = v.tone === "warn" ? "banner warn" : "banner";
  const bannerStyle = v.tone === "good"
    ? { background: "color-mix(in srgb, var(--good) 12%, transparent)", border: "1px solid color-mix(in srgb, var(--good) 35%, transparent)", color: "var(--ink)" }
    : v.tone === "neutral" ? { background: "var(--inset)", border: "1px solid var(--line)", color: "var(--ink)" } : undefined;
  const conf = (x) => <span style={{ color: "var(--ink-faint)", fontWeight: 400, fontSize: 11 }}> · conf {(x ?? 0).toFixed(2)}</span>;
  return (
    <div className="card">
      <div className="eyebrow">Voice × heart rate · Layer 4 cross-modal</div>
      <div className={bannerCls} style={bannerStyle}><div><b>{v.label}.</b> {v.detail}</div></div>
      <div className="metrics-grid" style={{ marginTop: 14 }}>
        <div className="mcell"><div className="mk">Voice · before</div><div className="mv">{cross.voice?.pre?.toFixed(1)}{conf(vc.pre)}</div></div>
        <div className="mcell"><div className="mk">Voice · after</div><div className="mv">{cross.voice?.post?.toFixed(1)}{conf(vc.post)}</div></div>
        <div className="mcell"><div className="mk">Heart · before</div><div className="mv">{cross.body?.pre?.toFixed(1)}</div></div>
        <div className="mcell"><div className="mk">Heart · after</div><div className="mv">{cross.body?.post?.toFixed(1)}</div></div>
        <div className="mcell"><div className="mk">Agreement</div><div className="mv">{cross.agreement}</div></div>
        <div className="mcell"><div className="mk">Verdict</div><div className="mv" style={{ fontSize: 13, textTransform: "capitalize" }}>{v.short}{cross.unresolved_mismatch ? ` (${cross.unresolved_mismatch.replace(/_/g, " ")})` : ""}</div></div>
      </div>
      <p className="muted" style={{ fontSize: 12.5, marginTop: 12, marginBottom: 0, lineHeight: 1.6 }}>
        Voice carries <b>valence</b> reliably; heart-rate variability carries <b>arousal</b>. When the voice reading is uncertain (low confidence), Layer 4 <b>defers to the heart signal</b> rather than asserting a cognitive–physiological mismatch — so an uncertain voice never raises a false alarm.
      </p>
    </div>
  );
}
function fmtDate(ts) { return new Date(ts).toLocaleString([], { month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit" }); }

/* ================= Welcome ================= */
export function Welcome({ participant, setParticipant, onStart, onHistory, sessionsCount }) {
  return (
    <section className="panel">
      <div className="card hero">
        <div className="eyebrow" style={{ justifyContent: "center", display: "flex" }}>CogniVoice · Component D</div>
        <h1 className="h-lead">A 30-second voice check-in, before and after your calm moment.</h1>
        <p className="lead-sub">You'll speak for about half a minute, take a short guided pause, then speak again — and see whether your stress eased. Your voice is analysed on this machine; nothing is shared.</p>
        <div style={{ margin: "26px auto 0", display: "grid", gap: 18, placeItems: "center" }}>
          <div className="field">
            <label htmlFor="pid">Participant name or ID (optional)</label>
            <input id="pid" value={participant} onChange={(e) => setParticipant(e.target.value)} placeholder="e.g. P01 or a nickname" />
          </div>
          <div className="row" style={{ justifyContent: "center" }}>
            <button className="btn primary" onClick={onStart}>Begin check-in →</button>
            <button className="btn ghost" onClick={onHistory}>View past sessions{sessionsCount ? ` (${sessionsCount})` : ""}</button>
          </div>
        </div>
      </div>
    </section>
  );
}

/* ================= Room check ================= */
export function RoomCheck({ ambient, busy, onCheck, onContinue }) {
  return (
    <section className="panel">
      <div className="card">
        <div className="eyebrow">Layer 1 · Quality gate</div>
        <h3 className="h-lead">First, let's check your room is quiet enough.</h3>
        <p className="lead-sub">Stay silent for a few seconds while the app listens to the background — or upload a sample of the room. A noisy space is caught here so it can't distort your reading.</p>
        <AudioInput withTranscript={false} duration={6} busy={busy} onSubmit={(f) => onCheck(f)} ctaLabel="Check my room" />
        {ambient && (
          <>
            <QualityBar ambient={ambient} />
            {ambient.ok
              ? <div className="banner" style={{ background: "color-mix(in srgb, var(--good) 12%, transparent)", border: "1px solid color-mix(in srgb, var(--good) 35%, transparent)", color: "var(--ink)" }}>
                  <div><b>Good to go.</b> The room is quiet enough for a clean recording.</div>
                </div>
              : <div className="banner warn"><div><b>Let's try that again.</b> {ambient.reasons?.join(" ") || "Reduce background noise and check again."}</div></div>}
            <div className="row" style={{ marginTop: 18 }}>
              <button className="btn primary" onClick={onContinue} disabled={!ambient.ok}>Continue to the check-in →</button>
              {!ambient.ok && <span className="or">Quiet the room and check again.</span>}
            </div>
          </>
        )}
      </div>
    </section>
  );
}

/* ================= Before / After check-in ================= */
export function CheckIn({ phase, data, busy, onAnalyze, onContinue }) {
  const warm = phase === "pre";
  const done = !!data?.result;
  return (
    <section className="panel">
      <div className="card">
        <div className="eyebrow">{warm ? "Before your session" : "After your session"} · voice check-in</div>
        <h3 className="h-lead">{PROMPTS[phase].title}</h3>
        <p className="lead-sub">{PROMPTS[phase].sub}</p>
        {!done
          ? <AudioInput withTranscript duration={30} busy={busy} onSubmit={onAnalyze} ctaLabel="Analyse my voice" />
          : (
            <>
              <div className="result" style={{ marginTop: 22, paddingTop: 20, borderTop: "1px solid var(--line-soft)" }}>
                <div className="transcript">
                  <div className="who">What you said</div>
                  <p>{data.transcript ? `"${data.transcript}"` : "(spoken — no transcript captured)"}</p>
                </div>
                <StressCard result={data.result} />
              </div>
              <div className="row" style={{ marginTop: 18 }}>
                <button className="btn primary" onClick={onContinue}>{warm ? "Continue to the calm moment →" : "See my full report →"}</button>
              </div>
            </>
          )}
        {data?.error && <div className="banner bad">{data.error}</div>}
      </div>
    </section>
  );
}

/* ================= Calm moment ================= */
export function CalmMoment({ onDone }) {
  const [left, setLeft] = useState(60);
  useEffect(() => { if (left <= 0) return; const t = setTimeout(() => setLeft((l) => l - 1), 1000); return () => clearTimeout(t); }, [left]);
  const mm = Math.floor(left / 60), ss = String(left % 60).padStart(2, "0");
  return (
    <section className="panel">
      <div className="card" style={{ textAlign: "center", padding: "48px 24px" }}>
        <div className="eyebrow warm" style={{ display: "flex", justifyContent: "center" }}>A calm moment · no headset needed</div>
        <h3 className="h-lead" style={{ marginTop: 8 }}>Breathe with the circle.</h3>
        <div style={{ display: "grid", placeItems: "center", margin: "28px 0 12px" }}><div className="breath-orb"><span>in… out…</span></div></div>
        <p className="lead-sub" style={{ margin: "0 auto", textAlign: "center" }}>The web build's stand-in for the VR meditation — a short guided pause. On the Quest, this slot is the meditation scene.</p>
        <div className="row" style={{ justifyContent: "center", marginTop: 24 }}>
          {left > 0 && <span className="chip"><span className="dot live" />{mm}:{ss} remaining</span>}
          <button className="btn primary" onClick={onDone}>{left > 0 ? "I'm ready now →" : "Continue →"}</button>
        </div>
      </div>
    </section>
  );
}

/* ================= Report (Layers 3–5, all scores) ================= */
export function Report({ full, pre, post, saved, loading, error, onNewSession, onHistory, onRetry }) {
  if (loading) return <section className="panel"><div className="card"><span className="spin" /> <span className="muted">Comparing before and after, checking heart data and your baseline…</span></div></section>;
  if (error) return <section className="panel"><div className="card"><div className="banner bad">{error}</div><div className="row" style={{ marginTop: 14 }}><button className="btn ghost" onClick={onRetry}>Try again</button></div></div></section>;
  if (!full) return null;

  const c = full.comparison || {};
  const improved = c.direction === "improved", worsened = c.direction === "worsened";
  const preScore = c.pre_stress ?? pre?.result?.stress_score ?? 0;
  const postScore = c.post_stress ?? post?.result?.stress_score ?? 0;
  const delta = c.delta ?? (postScore - preScore);
  const outcome = improved ? "improved" : worsened ? "worsened" : "steady";
  const cross = full.crossmodal, anomaly = full.anomaly, base = full.personal_baseline;

  const goodAnom = anomaly && (!anomaly.anomaly || anomaly.anomaly_direction === "unusual_improvement");
  const flagLabel = !anomaly ? "—" : !anomaly.anomaly ? "Pattern normal" : anomaly.anomaly_direction === "unusual_improvement" ? "Exceptional improvement" : "Review suggested";

  return (
    <section className="panel">
      <div className="card">
        <div className="spread">
          <div>
            <div className="eyebrow">Your session report</div>
            <h3 className="h-lead" style={{ fontSize: 28 }}>{improved ? "The session helped — your stress eased." : worsened ? "Your stress read higher after." : "Your stress stayed about the same."}</h3>
          </div>
          {saved && <span className="chip"><span className="dot live" />Saved to history</span>}
        </div>

        <div className="bigmetrics" style={{ marginTop: 20 }}>
          <BigMetric label="Before session" value={`${preScore.toFixed(1)}/10`} sub={pre?.result?.stress_level} tone="warn" />
          <BigMetric label="After session" value={`${postScore.toFixed(1)}/10`} sub={post?.result?.stress_level} tone={improved ? "good" : undefined} />
          <BigMetric label="Change" value={`${delta < 0 ? "↓" : delta > 0 ? "↑" : "→"} ${Math.abs(delta).toFixed(1)}`} sub={c.magnitude && c.magnitude !== "none" ? `${c.magnitude} change` : "within noise"} tone={improved ? "good" : worsened ? "warn" : undefined} />
          <BigMetric label="Outcome" value={outcome} sub={c.reliable ? "reliable" : "not reliable"} tone={improved ? "good" : worsened ? "warn" : undefined} />
        </div>
      </div>

      <div className="card">
        <div className="eyebrow">What each layer found</div>
        <div className="insights" style={{ marginTop: 16 }}>
          <InsightCard icon={improved ? "↓" : worsened ? "↑" : "→"} tone={improved ? "good" : worsened ? "warn" : "neutral"}
            title="Stress change (Layer 3)" value={improved ? "Reduced" : worsened ? "Increased" : "No major change"}
            text={c.reliable ? `A ${Math.abs(delta).toFixed(1)}-point ${improved ? "drop" : "change"}, above the honest noise floor — a real shift.` : "Below the reliable-change threshold given the model's confidence."} />
          {(() => { const v = crossVerdict(cross); return (
            <InsightCard icon={v.icon} tone={v.tone} title="Voice × heart (Layer 4)" value={v.short} text={v.detail} />
          ); })()}
          <InsightCard icon={goodAnom ? "✓" : "!"} tone={goodAnom ? "good" : "warn"}
            title="Session pattern (Layer 5)" value={flagLabel}
            text={!anomaly ? "Anomaly model warming up on simulated history." : !anomaly.anomaly ? "Follows the expected stress-reduction pattern." : anomaly.anomaly_direction === "unusual_improvement" ? "An unusually large improvement — a strong result, not a concern." : `Marked for review (${anomaly.severity}) — the pattern was unusual, not failed.`} />
        </div>
      </div>

      <CrossModalCard cross={cross} />

      {pre?.result && post?.result && (
        <div className="card">
          <div className="eyebrow">The signal behind the scores · valence &amp; arousal</div>
          <div className="plot-wrap" style={{ marginTop: 16 }}>
            <Circumplex points={[
              { valence: pre.result.valence, arousal: pre.result.arousal, label: "before", color: "var(--s-high)" },
              { valence: post.result.valence, arousal: post.result.arousal, label: "after", color: "var(--s-none)" },
            ]} />
            <div className="legend">
              <div className="leg" style={{ color: "var(--s-high)" }}><span className="swatch" style={{ background: "var(--s-high)" }} /><div><div className="lt">Before — {stressTypeLabel(pre.result.stress_type) || "neutral"}</div><div className="ld">valence {pre.result.valence.toFixed(2)}, arousal {pre.result.arousal.toFixed(2)}, confidence {pre.result.confidence.toFixed(2)}.</div></div></div>
              <div className="leg" style={{ color: "var(--s-none)" }}><span className="swatch" style={{ background: "var(--s-none)" }} /><div><div className="lt">After — {stressTypeLabel(post.result.stress_type) || "settling"}</div><div className="ld">valence {post.result.valence.toFixed(2)}, arousal {post.result.arousal.toFixed(2)}, confidence {post.result.confidence.toFixed(2)}.</div></div></div>
            </div>
          </div>
        </div>
      )}

      {/* full per-reading scores, side by side */}
      <div className="card">
        <div className="eyebrow">Every score · before and after</div>
        <div className="result" style={{ marginTop: 16 }}>
          {pre?.result && <div><div className="who" style={{ marginBottom: 8 }}>Before</div><StressCard result={pre.result} showReasons={false} /></div>}
          {post?.result && <div><div className="who" style={{ marginBottom: 8 }}>After</div><StressCard result={post.result} showReasons={false} /></div>}
        </div>
      </div>

      <div className="card">
        <p className="muted" style={{ margin: 0, lineHeight: 1.65 }}>
          <b style={{ color: "var(--ink)" }}>What this means.</b> This is a wellness research estimate, not a medical diagnosis. One session is a single data point;
          {base?.personalised ? ` compared with your own normal, this arrival reads as "${base.relative_band}".` : " your personal baseline is still being learned across sessions."} Sessions are saved so patterns can be reviewed over time.
        </p>
      </div>

      <TechnicalDetails panels={[
        { title: "Layer 2 — before reading", data: pre?.result },
        { title: "Layer 2 — after reading", data: post?.result },
        { title: "Layer 3 — comparison", data: full.comparison },
        { title: "Layer 4 — cross-modal", data: full.crossmodal },
        { title: "Layer 5 — anomaly", data: full.anomaly },
        { title: "Personal baseline", data: full.personal_baseline },
      ]} />

      <div className="row" style={{ marginTop: 20 }}>
        <button className="btn primary" onClick={onNewSession}>Start a new session</button>
        <button className="btn ghost" onClick={onHistory}>View saved sessions</button>
      </div>
    </section>
  );
}

/* ================= History ================= */
export function History({ sessions, onOpen, onClear, onBack }) {
  return (
    <section className="panel">
      <div className="card">
        <div className="spread">
          <div><div className="eyebrow">Saved sessions</div><h3 className="h-lead" style={{ fontSize: 26 }}>Session history</h3><p className="lead-sub">Stored locally in this browser — useful for tracking each participant over repeated sessions.</p></div>
          {sessions.length > 0 && <button className="btn ghost" onClick={onClear}>Clear history</button>}
        </div>
        {sessions.length === 0
          ? <div className="empty">No sessions saved yet. Complete a check-in and it'll appear here.</div>
          : (
            <div className="hist-grid" style={{ marginTop: 20 }}>
              {sessions.map((s) => {
                const anom = s.full?.anomaly?.anomaly && s.full?.anomaly?.anomaly_direction !== "unusual_improvement";
                const out = s.full?.comparison?.direction === "improved" ? "improved" : s.full?.comparison?.direction === "worsened" ? "worsened" : "steady";
                return (
                  <div className="hist" key={s.id}>
                    <div className="ht">
                      <div><div className="hdate">{fmtDate(s.at)}</div><div className="hout">{out}</div>{s.participant && <div className="hdate" style={{ marginTop: 2 }}>· {s.participant}</div>}</div>
                      <span className={`spill ${anom ? "warn" : "good"}`}>{anom ? "Review" : "Normal"}</span>
                    </div>
                    <div className="htri">
                      <div className="hc"><div className="hk">Before</div><div className="hv">{s.pre?.stress_score?.toFixed(1)}</div></div>
                      <div className="hc"><div className="hk">After</div><div className="hv">{s.post?.stress_score?.toFixed(1)}</div></div>
                      <div className="hc"><div className="hk">Δ</div><div className="hv">{s.full?.comparison?.delta?.toFixed(1)}</div></div>
                    </div>
                    <button className="btn ghost" style={{ marginTop: 16, width: "100%", justifyContent: "center" }} onClick={() => onOpen(s)}>Open report</button>
                  </div>
                );
              })}
            </div>
          )}
        <div className="row" style={{ marginTop: 20 }}><button className="btn ghost" onClick={onBack}>← Back</button></div>
      </div>
    </section>
  );
}

/* ================= The research (PP1 → PP2, full progression) ================= */
export function Research() {
  return (
    <section className="panel">
      <div className="card">
        <div className="eyebrow warm">The research · PP1 → PP2 (current)</div>
        <h3 className="h-lead">From a six-experiment encoder search to an honest, multimodal-by-design stress model.</h3>
        <p className="lead-sub">PP1 asked me to prove voice&nbsp;→&nbsp;emotion with ML <em>deeply</em> — not just report a number. So I ran a controlled ablation, kept the encoder the evidence chose, replaced the hand-written stress mapping with a learned one, then tested it on <b>real</b> voices (English + zero-shot Sinhala) and diagnosed exactly where and why it breaks. Every figure below is measured; negative results are kept in.</p>

        <div className="sub-h"><span className="tick">01</span> PP1 — the encoder search (acted English, speaker-independent)</div>
        <p className="muted">Six experiments on acted corpora (RAVDESS + CREMA-D + TESS). The encoder choice dominated everything else.</p>
        <div className="rtable-wrap">
          <table className="rt">
            <thead><tr><th>Experiment</th><th>Encoder</th><th>Result</th><th></th></tr></thead>
            <tbody>
              <tr><td className="m">wav2vec2 + MLP</td><td>wav2vec2-base</td><td>60.9% val acc</td><td><span className="pill2 flop">weak</span></td></tr>
              <tr><td className="m">hand-feature MLP</td><td>DSP features</td><td>70.8% val acc</td><td><span className="pill2 part">baseline</span></td></tr>
              <tr><td className="m"><b>emotion2vec + MLP</b></td><td>emotion2vec-base</td><td><b>84.7% acc · macro-F1 83.9%</b></td><td><span className="pill2 good">winner</span></td></tr>
              <tr><td className="m">V/A regression <span style={{ color: "var(--ink-faint)" }}>(shipped PP1)</span></td><td>emotion2vec-base</td><td>80.4% binary · R² 0.57</td><td><span className="pill2 part">shipped</span></td></tr>
              <tr><td className="m">+ calm augmentation</td><td>emotion2vec-base</td><td>R² 0.53 · MAE 0.094</td><td><span className="pill2 part">marginal</span></td></tr>
              <tr><td className="m">+ LibriSpeech</td><td>emotion2vec-base</td><td>R² 0.55 · MAE 0.091</td><td><span className="pill2 part">marginal</span></td></tr>
            </tbody>
          </table>
        </div>
        <p className="muted" style={{ fontSize: 13 }}>emotion2vec beat wav2vec2 by <b>+24 points</b> for the same task → kept for PP2. Extra data/augmentation barely moved R² (0.53→0.55): the bottleneck was data <em>nature</em> (acted vs natural), not quantity. The shipped PP1 model used a <b>hand-written</b> emotion→stress table — the exact thing the panel flagged.</p>

        <div className="sub-h"><span className="tick">02</span> PP2 — the enhancement the panel asked for</div>
        <p className="muted">Frozen <b>emotion2vec_plus_large</b> (1024-d, 42,500 h) + a trainable <b>prosody branch</b> (F0, jitter, shimmer, rate) + <b>gated fusion</b> + a <b>learned</b> valence/arousal head — replacing the hand-written lookup. In-domain it trains to near-ceiling:</p>
        <div className="kpi-row">
          <div className="kpi2"><div className="v" style={{ color: "var(--s-none)" }}>0.864</div><div className="k">CCC valence (held-out acted)</div></div>
          <div className="kpi2"><div className="v" style={{ color: "var(--s-none)" }}>0.810</div><div className="k">CCC arousal (held-out acted)</div></div>
          <div className="kpi2"><div className="v" style={{ color: "var(--s-none)" }}>0.92</div><div className="k">binary stress F1 (in-domain)</div></div>
          <div className="kpi2"><div className="v" style={{ color: "var(--teal)" }}>~1–2M</div><div className="k">trainable params · encoder frozen</div></div>
        </div>

        <div className="sub-h"><span className="tick">03</span> The acid test — in-domain metrics INVERT on real voices</div>
        <p className="muted">All checkpoints scored on 24 genuine/TTS clips outside every training set (17 stressed, 7 calm; 11 Sinhala, zero-shot). Each model's polished <b>in-domain arousal CCC</b> next to its <b>real-voice</b> accuracy — the relationship is <b>inverted</b>:</p>
        <div className="rtable-wrap">
          <table className="rt">
            <thead><tr><th>Checkpoint</th><th>In-domain CCC-arousal</th><th>Real voices</th><th></th></tr></thead>
            <tbody>
              <tr><td className="m">fusion_acted</td><td>0.81 — best</td><td>38% — worst</td><td><span className="pill2 flop">flop</span></td></tr>
              <tr><td className="m">fusion_v2 <span style={{ color: "var(--ink-faint)" }}>(was active)</span></td><td>0.79</td><td>75% · Sinhala 64%</td><td><span className="pill2 part">partial</span></td></tr>
              <tr><td className="m">fusion_combined</td><td>0.77</td><td>63%</td><td><span className="pill2 part">partial</span></td></tr>
              <tr><td className="m">fusion_iemocap</td><td>0.65</td><td>58%</td><td><span className="pill2 part">partial</span></td></tr>
              <tr className="ship"><td className="m"><b>fusion_meld_baseline</b></td><td><b>0.35 — worst</b></td><td><b>92% — best</b></td><td><span className="pill2 good">active</span></td></tr>
            </tbody>
          </table>
        </div>
        <p className="muted" style={{ fontSize: 13 }}>Selecting a model on acted metrics is <b>actively harmful</b>. The high-CCC models overfit the "loud acted = aroused" studio style; <b>fusion_meld_baseline</b> trained on natural speech, so its <b>valence</b> generalises.</p>

        <div className="sub-h"><span className="tick">04</span> Two axes, one reliable — valence transfers, arousal collapses</div>
        <p className="muted">Held across all checkpoints and <b>both languages</b>: stressed voices read as negative valence <b>94–100%</b> of the time, but arousal is positive on only <b>12–53%</b> of them — quiet "freeze" stress reads as low-energy, so any score leaning on arousal dies.</p>
        <div className="ba">
          <div className="b bad"><div className="lbl">Before — neutral reads as stress</div><code>stress = (1 − valence)/2 · (…arousal)</code><p className="r" style={{ color: "var(--s-high)" }}>calm voice → ~4 / 10 ✕ (thin separation)</p></div>
          <div className="b good"><div className="lbl">After — valence-primary</div><code>stress = max(0, − valence)</code><p className="r" style={{ color: "var(--s-none)" }}>calm → 0 · stressed → high ✓</p></div>
        </div>
        <p className="muted">Threshold-free separation (d′) rose on <b>every</b> checkpoint under valence-primary scoring — the active model from <b>1.86 → 3.62</b>. Arousal no longer drives magnitude; it names the <b>type</b> — activated (fight-or-flight) vs withdrawn (freeze) — and becomes the axis that cross-checks against the heart.</p>
      </div>

      <div className="card">
        <div className="eyebrow">Phase 1 — the fix · leave-one-out on real English + Sinhala</div>
        <p className="muted">Valence-primary scoring + an LOO-calibrated 2.0 boundary (fit on N−1, tested on the held-out clip). <b>No retraining</b> — purely selecting on real-voice evidence and scoring on the reliable axis.</p>
        <div className="kpi-row">
          <div className="kpi2"><div className="v"><span style={{ color: "var(--ink-soft)" }}>75%</span> <span className="arrowto">→</span> <span style={{ color: "var(--s-none)" }}>91.7%</span></div><div className="k">overall real-voice accuracy (LOO)</div></div>
          <div className="kpi2"><div className="v"><span style={{ color: "var(--s-high)" }}>64%</span> <span className="arrowto">→</span> <span style={{ color: "var(--s-none)" }}>91%</span></div><div className="k">Sinhala · zero-shot · was failing the KPI</div></div>
          <div className="kpi2"><div className="v" style={{ color: "var(--s-none)" }}>16 / 17</div><div className="k">stressed clips caught · 6 / 7 calm</div></div>
          <div className="kpi2"><div className="v" style={{ color: "var(--teal)" }}>✓ 75% KPI</div><div className="k">passed in both languages</div></div>
        </div>
        <p className="muted">Sinhala works zero-shot because valence rides a language-independent signal and the prosody branch (pitch, tremor, rate) is the same physiology in any language.</p>
      </div>

      <div className="card">
        <div className="eyebrow">Phase 2 — does adding Sinhala training data help? (negative result)</div>
        <p className="muted">The honest test of the known weak spot. I collected <b>26 clips from 7 new speakers</b>, mixed them into training (speaker-independent — eval speakers untouched), retrained the head. Evaluated on the 11 held-out Sinhala clips:</p>
        <div className="rtable-wrap">
          <table className="rt">
            <thead><tr><th>Model</th><th>Accuracy</th><th>Stressed recall</th><th>Calm specificity</th></tr></thead>
            <tbody>
              <tr className="ship"><td className="m"><b>meld_baseline</b> <span style={{ color: "var(--ink-faint)" }}>(MELD only, shipped)</span></td><td><b>90.9%</b></td><td>87.5%</td><td>100%</td></tr>
              <tr><td className="m">+ 26 Sinhala · graded labels</td><td>81.8%</td><td>75.0%</td><td>100%</td></tr>
              <tr><td className="m">+ 26 Sinhala · binary labels</td><td>81.8%</td><td>87.5%</td><td>66.7%</td></tr>
            </tbody>
          </table>
        </div>
        <p className="muted" style={{ fontSize: 13 }}>The data <b>slightly hurt</b> held-out Sinhala. A controlled label ablation (identical seed) lands <b>both</b> variants at 81.8% — so the label scheme only <em>relocates</em> the error; it doesn't change the verdict. <b>The graded-label hypothesis is rejected.</b> A handful of clips cannot teach a <em>frozen</em> encoder that is out-of-distribution for Sinhala — a legitimate finding, not a fixable bug.</p>
      </div>

      <div className="card">
        <div className="eyebrow">Phase 3 — a confident single-speaker misread (English OOD, live)</div>
        <p className="muted">Live-testing my own Sri-Lankan-accented English — two stressed clips and one genuinely calm/relieved clip. The model pinned <em>all three</em> at valence ≈ −0.9 with <b>high</b> confidence:</p>
        <div className="rtable-wrap">
          <table className="rt">
            <thead><tr><th>Clip</th><th>True state</th><th>Valence</th><th>Confidence</th><th>Stress /10</th></tr></thead>
            <tbody>
              <tr><td className="m">before</td><td>stressed</td><td>−0.855</td><td>0.86</td><td>8.55</td></tr>
              <tr><td className="m"><b>after</b></td><td><b>calm / relieved</b></td><td><b>−0.913</b></td><td><b>0.91</b></td><td><b>9.13</b></td></tr>
              <tr><td className="m">before</td><td>stressed</td><td>−0.934</td><td>0.93</td><td>9.34</td></tr>
            </tbody>
          </table>
        </div>
        <p className="muted" style={{ fontSize: 13 }}>Same root cause as Sinhala — the frozen encoder is OOD for this individual voice, so it locks onto speaker identity and collapses within-speaker <em>variation</em>. It is <b>worse</b> than the Sinhala case: Sinhala failed with <em>low</em> confidence so Layer 4 deferred to HRV, but this misread is <em>confident</em> (0.91 &gt; CONF_MIN 0.4) so the defer gate doesn't fire. n = 3, one speaker — anecdotal, does not overturn the 92% population result, but recorded openly. Fix (speaker-relative baseline / OOD term in confidence) is stated as future work.</p>
      </div>

      <div className="card">
        <div className="eyebrow">The multimodal answer, limitations, and the contribution</div>
        <ul className="li-tight">
          <li><b>Confidence = |valence|</b> — the reliable axis. Near-neutral voices read low-confidence, exactly when they should, so <b>Layer 4 defers to Component B's HRV</b> rather than raising a false flag. The failure <em>is</em> the reason the system is multimodal.</li>
          <li><b>n = 24</b> real clips (Sinhala 11, ~3 speakers): LOO removes fit-to-test bias but not small-sample variance — directional, not tight.</li>
          <li><b>Binary stressed/calm is validated</b>; mild/moderate/high severity bands are provisional until graded labels or paired HRV arrive. Arousal is offloaded to Component B by design. <b>Layer 5</b> runs on simulated sessions until real longitudinal data exists.</li>
        </ul>
        <p className="callout">The contribution isn't only a model — it's the <b>diagnosis</b>: I proved with numbers that voice reliably encodes <b>valence</b> but not <b>arousal</b> for internalised stress, that acted metrics invert on real voices, and that the honest fix is complementary multimodal sensing (voice → valence, HRV → arousal) with per-signal confidence. Encoder frozen; scoring + fusion designed and validated by me; evaluated on real, unseen, multilingual voices — failures stated openly.</p>
        <div className="rmeta"><span>PP1 six-experiment ablation</span><span>PP2 five-checkpoint real-voice test</span><span>English + Sinhala, zero-shot</span><span>94 automated tests</span></div>
      </div>
    </section>
  );
}
