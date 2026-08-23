// CogniVoice — Component D demo app. Owns session state; orchestrates the flow
// against the live API. Screens in steps.jsx; visuals in ui.jsx.

import { useCallback, useEffect, useRef, useState } from "react";
import { getHealth, ambientCheck, infer, fullSession, API_BASE, POLL_B } from "./api.js";
import { TopBar, Rail } from "./ui.jsx";
import { Welcome, RoomCheck, CheckIn, CalmMoment, Report, History, Research } from "./steps.jsx";

const RAIL = [
  { id: "room", label: "Room check", desc: "Quiet enough?", mark: "1" },
  { id: "before", label: "Before check-in", desc: "Record your voice", mark: "2" },
  { id: "calm", label: "Calm moment", desc: "Guided breathing", mark: "3" },
  { id: "after", label: "After check-in", desc: "Record your voice", mark: "4" },
  { id: "report", label: "Your report", desc: "All the scores", mark: "5" },
  { id: "history", label: "Past sessions", desc: "Saved locally", mark: "✦", group: "panel", sep: "For you" },
  { id: "research", label: "The research", desc: "PP1 → PP2 story", mark: "✦", group: "panel" },
];
const STORE = "cognivoice_sessions";
const loadSessions = () => { try { return JSON.parse(localStorage.getItem(STORE) || "[]"); } catch { return []; } };

export default function App() {
  const [theme, setTheme] = useState(() => window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  useEffect(() => { document.documentElement.setAttribute("data-theme", theme); }, [theme]);

  const [health, setHealth] = useState(null);
  const [userId] = useState(() => { let u = localStorage.getItem("cv_user"); if (!u) { u = "u_" + Math.random().toString(36).slice(2, 9); localStorage.setItem("cv_user", u); } return u; });
  const [participant, setParticipant] = useState("");
  const [language, setLanguage] = useState("english");   // english | sinhala (for the test log)
  const [selfPre, setSelfPre] = useState("");            // subject's own 0-10 stress before
  const [selfPost, setSelfPost] = useState("");          //   ...and after (ground truth)
  const [sessionId, setSessionId] = useState(() => crypto.randomUUID());

  const [step, setStep] = useState(() => (window.location.hash.replace("#", "") === "research" ? "research" : "welcome"));
  const [ambient, setAmbient] = useState(null);
  const [ambientBusy, setAmbientBusy] = useState(false);
  const [pre, setPre] = useState(null);
  const [post, setPost] = useState(null);
  const [busy, setBusy] = useState(false);
  const [calmDone, setCalmDone] = useState(false);
  const [full, setFull] = useState(null);
  const [fullLoading, setFullLoading] = useState(false);
  const [fullError, setFullError] = useState("");
  const [saved, setSaved] = useState(false);
  const [sessions, setSessions] = useState(loadSessions);
  const savedFor = useRef(null);

  useEffect(() => {
    let alive = true;
    const load = () => getHealth().then((h) => alive && setHealth(h)).catch(() => alive && setHealth(null));
    load(); const id = setInterval(load, 15000);
    return () => { alive = false; clearInterval(id); };
  }, []);

  const reachable = useCallback((id) => {
    switch (id) {
      case "room": return true;
      case "before": return !!ambient?.ok;
      case "calm": case "after": return !!pre?.result;
      case "report": return !!(pre?.result && post?.result);
      case "history": case "research": return true;
      default: return false;
    }
  }, [ambient, pre, post]);

  const go = (id) => { if (reachable(id)) { setStep(id); if (id === "research") window.location.hash = "research"; else if (window.location.hash) window.location.hash = ""; } };

  const doneMap = { room: !!ambient?.ok, before: !!pre?.result, calm: calmDone, after: !!post?.result, report: !!full };
  const steps = RAIL.map((s) => ({ ...s, done: !!doneMap[s.id] }));

  const onAmbient = useCallback(async (file) => {
    setAmbientBusy(true);
    try { setAmbient(await ambientCheck(file)); }
    catch (e) { setAmbient({ ok: false, reasons: [e.message] }); }
    finally { setAmbientBusy(false); }
  }, []);

  const analyze = (phase) => async (file, transcript) => {
    setBusy(true);
    const setter = phase === "pre" ? setPre : setPost;
    try {
      // POLL_B on: D pulls the REAL Component B reading at this phase moment.
      // Off (default): the heart-rate signal comes from Component B's simulated
      // provider, so Layer 4 is still a real cross-check (not voice vs itself).
      // log=true persists the clip + scores so the live test yields labelled data.
      const result = await infer(file, sessionId, phase, {
        pollB: POLL_B, log: true, userId: participant || userId, language,
      });
      setter({ result, transcript });
    } catch (e) { setter({ error: e.message }); }
    finally { setBusy(false); }
  };

  // Report: run Layers 3–5 once, then persist the session to history.
  useEffect(() => {
    if (step === "report" && pre?.result && post?.result && !full && !fullLoading && !fullError) {
      setFullLoading(true);
      // Real B poll happened at /infer -> use stored readings (mock off); else
      // fall back to the simulated HRV provider for a solo demo.
      fullSession(sessionId, participant || userId, {
        useMockHrv: !POLL_B, language, selfPre, selfPost, log: true,
      })
        .then((f) => {
          setFull(f);
          if (savedFor.current !== sessionId) {
            savedFor.current = sessionId;
            const entry = { id: sessionId, at: Date.now(), participant, language, selfPre, selfPost, pre: pre.result, post: post.result, full: f };
            setSessions((prev) => { const next = [entry, ...prev].slice(0, 50); localStorage.setItem(STORE, JSON.stringify(next)); return next; });
            setSaved(true);
          }
        })
        .catch((e) => setFullError(e.message))
        .finally(() => setFullLoading(false));
    }
  }, [step, pre, post, full, fullLoading, fullError, sessionId, userId, participant, language, selfPre, selfPost]);

  const newSession = () => {
    setSessionId(crypto.randomUUID());
    setAmbient(null); setPre(null); setPost(null); setFull(null);
    setFullError(""); setCalmDone(false); setSaved(false); savedFor.current = null;
    setSelfPre(""); setSelfPost("");   // keep language + participant for the next take
    setStep("welcome");
  };
  const openSaved = (s) => { setPre({ result: s.pre }); setPost({ result: s.post }); setFull(s.full); setSaved(true); savedFor.current = s.id; setStep("report"); };
  const clearHistory = () => { localStorage.removeItem(STORE); setSessions([]); };

  const layers = health?.layers || {};
  const pipe = [
    { n: "1", label: "Quality gate", on: layers.layer1_quality },
    { n: "2", label: "Stress model", on: layers.layer2_fusion },
    { n: "3", label: "Pre / post", on: layers.layer3_compare },
    { n: "4", label: "Voice × heart", on: layers.layer4_crossmodal },
    { n: "5", label: "Anomaly", on: layers.layer5_anomaly },
  ];

  const main = (
    <>
      {step === "room" && <RoomCheck ambient={ambient} busy={ambientBusy} onCheck={onAmbient} onContinue={() => go("before")} />}
      {step === "before" && <CheckIn phase="pre" data={pre} busy={busy} onAnalyze={analyze("pre")} onContinue={() => go("calm")} selfRating={selfPre} setSelfRating={setSelfPre} />}
      {step === "calm" && <CalmMoment onDone={() => { setCalmDone(true); go("after"); }} />}
      {step === "after" && <CheckIn phase="post" data={post} busy={busy} onAnalyze={analyze("post")} onContinue={() => go("report")} selfRating={selfPost} setSelfRating={setSelfPost} />}
      {step === "report" && <Report full={full} pre={pre} post={post} saved={saved} loading={fullLoading} error={fullError}
        onNewSession={newSession} onHistory={() => go("history")} onRetry={() => { setFullError(""); setFull(null); }} />}
      {step === "history" && <History sessions={sessions} onOpen={openSaved} onClear={clearHistory} onBack={() => go(pre?.result ? "report" : "room")} />}
      {step === "research" && <Research />}
    </>
  );

  return (
    <div className="wrap">
      <div className="shell">
        <TopBar health={health} theme={theme}
          onToggleTheme={() => setTheme((t) => (t === "dark" ? "light" : "dark"))} />

        {!health && (
          <div className="banner bad" style={{ marginBottom: 18 }}>
            Can't reach the Component D server on <code>{API_BASE}</code>. Start it, then this turns green.
          </div>
        )}

        {step === "welcome"
          ? <Welcome participant={participant} setParticipant={setParticipant}
              language={language} setLanguage={setLanguage} sessionsCount={sessions.length}
              onStart={() => go("room")} onHistory={() => go("history")} />
          : (
            <div className="grid">
              <Rail steps={steps} current={step} onGo={go} reachable={reachable} />
              <main>{main}</main>
            </div>
          )}

        <div className="pipe">
          <span className="pl">Under the hood</span>
          {pipe.map((p, i) => (
            <span key={p.n} style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
              <span className={`node${p.on ? "" : " off"}`}><span className="n">{p.n}</span>{p.label}</span>
              {i < pipe.length - 1 && <span className="arrow">→</span>}
            </span>
          ))}
          <button className="ghost-btn" style={{ marginLeft: "auto" }} onClick={newSession}>↺ New session</button>
        </div>
        <p className="note">CogniVoice · Component&nbsp;D — voice-based adaptive stress detection · SLIIT dissertation R26-DS-006. Research prototype; on-device inference, not a medical device.</p>
      </div>
    </div>
  );
}
