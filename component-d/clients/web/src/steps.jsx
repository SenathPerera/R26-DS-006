// The screens of the session flow. App.jsx owns state + passes data/callbacks.

import { useEffect, useState } from "react";
import { AudioInput, StressCard, QualityBar, Circumplex, BigMetric, InsightCard, TechnicalDetails } from "./ui.jsx";

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
              <div className="leg" style={{ color: "var(--s-high)" }}><span className="swatch" style={{ background: "var(--s-high)" }} /><div><div className="lt">Before — {pre.result.stress_type || "neutral"}</div><div className="ld">valence {pre.result.valence.toFixed(2)}, arousal {pre.result.arousal.toFixed(2)}, confidence {pre.result.confidence.toFixed(2)}.</div></div></div>
              <div className="leg" style={{ color: "var(--s-none)" }}><span className="swatch" style={{ background: "var(--s-none)" }} /><div><div className="lt">After — {post.result.stress_type || "settling"}</div><div className="ld">valence {post.result.valence.toFixed(2)}, arousal {post.result.arousal.toFixed(2)}, confidence {post.result.confidence.toFixed(2)}.</div></div></div>
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

/* ================= The research (PP1 → PP2, current) ================= */
export function Research() {
  return (
    <section className="panel">
      <div className="card">
        <div className="eyebrow warm">The research · PP1 → PP2</div>
        <h3 className="h-lead">What I found, why it broke on real voices, and how I fixed it.</h3>
        <p className="lead-sub">PP1 asked me to prove voice&nbsp;→&nbsp;emotion with ML <em>deeply</em> — not just report a number. So I ran a controlled six-model ablation, tested every model on <b>real</b> voices (English + zero-shot Sinhala), found <em>why</em> it breaks, and fixed it with the evidence.</p>

        <div className="sub-h"><span className="tick">01</span> The finding — the best acted model is the worst on real voices</div>
        <p className="muted">I scored all six checkpoints on 24 real clips and put each model's polished <b>in-domain</b> score next to its <b>real-voice</b> accuracy. The relationship is <b>inverted</b> — chasing acted metrics actively hurts.</p>
        <div className="rtable-wrap">
          <table className="rt">
            <thead><tr><th>Model</th><th>In-domain (acted CCC)</th><th>Real voices</th><th></th></tr></thead>
            <tbody>
              <tr><td className="m">Acted</td><td>0.81 — best</td><td>38% — worst</td><td><span className="pill2 flop">flop</span></td></tr>
              <tr><td className="m">v2 (was active)</td><td>0.79</td><td>75% · fails Sinhala (64%)</td><td><span className="pill2 part">partial</span></td></tr>
              <tr><td className="m">Combined</td><td>0.77</td><td>63%</td><td><span className="pill2 part">partial</span></td></tr>
              <tr><td className="m">IEMOCAP</td><td>0.65</td><td>58%</td><td><span className="pill2 part">partial</span></td></tr>
              <tr className="ship"><td className="m"><b>MELD-baseline</b></td><td><b>0.35 — worst</b></td><td><b>92% — best</b></td><td><span className="pill2 good">active</span></td></tr>
            </tbody>
          </table>
        </div>
        <p className="muted" style={{ fontSize: 13 }}>The high-CCC models overfit the "loud acted = aroused" studio style. <b>MELD-baseline</b> trained on natural speech, so its <b>valence</b> generalises to real voices — which is what actually matters.</p>

        <div className="sub-h"><span className="tick">02</span> Two axes, one reliable — valence works, arousal collapses</div>
        <p className="muted">Across all six models and <b>both languages</b>: valence (pleasant ↔ unpleasant) is right ~<b>100%</b> of the time on stressed voices. But arousal <b>collapses</b> — quiet, internalised "freeze" stress reads as low-energy, so any score that leans on arousal dies.</p>
        <div className="ba">
          <div className="b bad"><div className="lbl">Before — neutral reads as stress</div><code>stress = (1 − valence)/2 · (…arousal)</code><p className="r" style={{ color: "var(--s-high)" }}>calm voice → ~4 / 10 ✕ (thin separation)</p></div>
          <div className="b good"><div className="lbl">After — valence-primary</div><code>stress = max(0, − valence)</code><p className="r" style={{ color: "var(--s-none)" }}>calm → 0 · stressed → high ✓</p></div>
        </div>
        <p className="muted">Arousal no longer drives the score — it names the <b>type</b> ("activated" vs the "shutdown" freeze response) and becomes the axis that cross-checks against the heart. Fight, flight, <b>or freeze.</b></p>

        <div className="sub-h"><span className="tick">03</span> The multimodal answer — voice for valence, heart for arousal</div>
        <ul className="li-tight">
          <li><b>Confidence tracks |valence|</b> — the reliable axis. Near-neutral voices read as low-confidence, exactly when they should.</li>
          <li><b>Layer 4 is confidence-gated</b> — a voice/heart disagreement is only called a <b>cognitive–physiological mismatch</b> when both signals are confident; an uncertain voice <b>defers to the heart</b> instead of raising a false flag.</li>
          <li>So the arousal my voice can't resolve is supplied by <b>Component B (HRV)</b> — the failure <em>is</em> the reason the system is multimodal.</li>
        </ul>
      </div>

      <div className="card">
        <div className="eyebrow">The recovery · leave-one-out on real English + Sinhala</div>
        <div className="kpi-row">
          <div className="kpi2"><div className="v"><span style={{ color: "var(--ink-soft)" }}>75%</span> <span className="arrowto">→</span> <span style={{ color: "var(--s-none)" }}>92%</span></div><div className="k">overall real-voice accuracy (LOO)</div></div>
          <div className="kpi2"><div className="v"><span style={{ color: "var(--s-high)" }}>64%</span> <span className="arrowto">→</span> <span style={{ color: "var(--s-none)" }}>91%</span></div><div className="k">Sinhala · zero-shot · was failing the KPI</div></div>
          <div className="kpi2"><div className="v" style={{ color: "var(--s-none)" }}>16 / 17</div><div className="k">stressed clips caught · 6/7 calm</div></div>
          <div className="kpi2"><div className="v" style={{ color: "var(--teal)" }}>✓ 75%</div><div className="k">KPI target · passed in both languages</div></div>
        </div>
        <p className="muted">No retraining — purely by <b>selecting the model on real-voice evidence</b> and fixing the scoring. Sinhala works zero-shot because valence is carried by a language-independent signal; the prosody branch (pitch, tremor, rate) is the same physiology in any language.</p>
      </div>

      <div className="card">
        <div className="eyebrow">Honest limitations, and the contribution</div>
        <ul className="li-tight">
          <li><b>n = 24</b> real clips (Sinhala 11, ~3 speakers). Leave-one-out removes fit-to-test bias but not small-sample variance — directional, not tight. More Sinhala data is the next step.</li>
          <li><b>Binary stressed/calm is validated</b>; the mild/moderate/high severity bands are provisional until graded labels or paired HRV exist.</li>
          <li><b>Layer 5 anomaly</b> runs on simulated sessions until real longitudinal data exists.</li>
        </ul>
        <p className="callout">The contribution isn't only a model — it's the <b>diagnosis</b>: I proved with numbers that voice reliably encodes <b>valence</b> but not <b>arousal</b> for internalised stress, that acted metrics invert on real voices, and that the honest fix is complementary multimodal sensing (voice → valence, HRV → arousal) with per-signal confidence. Encoder frozen; scoring + fusion designed and validated by me; evaluated on real, unseen, multilingual voices — failures stated openly.</p>
        <div className="rmeta"><span>Six-model ablation</span><span>94 automated tests</span><span>English + Sinhala real-voice, zero-shot</span></div>
      </div>
    </section>
  );
}
