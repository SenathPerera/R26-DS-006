// Audio + speech helpers. The recorder and WAV encoder are ported verbatim
// from the PP1 app (proven against this backend's soundfile WAV fast-path).
// Browser TTS speaks the companion; browser STT transcribes the student (with
// a typed fallback in the UI, so a flaky recogniser never stalls the demo).

import { useEffect, useRef, useState } from "react";

const clamp = (n, min, max) => Math.max(min, Math.min(max, n));

// ---------------- text to speech (companion voice) ----------------
function getPreferredVoice() {
  if (!("speechSynthesis" in window)) return null;
  const voices = window.speechSynthesis.getVoices();
  const preferred = ["Samantha", "Karen", "Moira", "Microsoft Zira",
    "Google UK English Female", "Google US English"];
  for (const name of preferred) {
    const found = voices.find((v) => v.name.toLowerCase().includes(name.toLowerCase()));
    if (found) return found;
  }
  return voices.find((v) => v.lang.toLowerCase().startsWith("en")) || voices[0] || null;
}

export function speak(text, { onStart, onDone } = {}) {
  if (!("speechSynthesis" in window) || !text) { onDone?.(); return; }
  window.speechSynthesis.cancel();
  const u = new SpeechSynthesisUtterance(text);
  u.rate = 0.92; u.pitch = 1.04; u.volume = 0.95;
  const v = getPreferredVoice();
  if (v) u.voice = v;
  u.onstart = () => onStart?.();
  u.onend = () => onDone?.();
  u.onerror = () => onDone?.();
  window.speechSynthesis.speak(u);
}

export function stopSpeaking() {
  if ("speechSynthesis" in window) window.speechSynthesis.cancel();
}

// ---------------- WAV encoding ----------------
function encodeWav(samples, sampleRate) {
  const bytesPerSample = 2;
  const buffer = new ArrayBuffer(44 + samples.length * bytesPerSample);
  const view = new DataView(buffer);
  const writeString = (o, s) => { for (let i = 0; i < s.length; i++) view.setUint8(o + i, s.charCodeAt(i)); };
  writeString(0, "RIFF");
  view.setUint32(4, 36 + samples.length * bytesPerSample, true);
  writeString(8, "WAVE"); writeString(12, "fmt ");
  view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
  view.setUint32(24, sampleRate, true); view.setUint32(28, sampleRate * bytesPerSample, true);
  view.setUint16(32, bytesPerSample, true); view.setUint16(34, 16, true);
  writeString(36, "data"); view.setUint32(40, samples.length * bytesPerSample, true);
  let offset = 44;
  for (let i = 0; i < samples.length; i++, offset += 2) {
    const s = Math.max(-1, Math.min(1, samples[i]));
    view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7fff, true);
  }
  return new Blob([view], { type: "audio/wav" });
}

function mergeFloat32(chunks) {
  const length = chunks.reduce((sum, a) => sum + a.length, 0);
  const out = new Float32Array(length);
  let offset = 0;
  chunks.forEach((a) => { out.set(a, offset); offset += a.length; });
  return out;
}

export function blobToFile(blob, name) {
  return new File([blob], name, { type: blob.type || "audio/wav" });
}

// ---------------- microphone recorder (raw WAV) ----------------
const BARS = 40;

// A SINGLE shared AudioContext for the whole app. Creating a NEW one per
// recording hits Chrome's per-page context limit (~6) and silently breaks the
// mic on later sessions — the "second session doesn't record" bug. Create once,
// reuse, and never close it.
let sharedCtx = null;
function getAudioContext() {
  const AC = window.AudioContext || window.webkitAudioContext;
  if (!sharedCtx || sharedCtx.state === "closed") sharedCtx = new AC();
  return sharedCtx;
}

export function useRecorder() {
  const [isRecording, setRecording] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [blob, setBlob] = useState(null);
  const [levels, setLevels] = useState(Array.from({ length: BARS }, () => 0.06));
  const [error, setError] = useState("");

  const stream = useRef(null), analyser = useRef(null), source = useRef(null);
  const processor = useRef(null), gain = useRef(null), chunks = useRef([]);
  const timer = useRef(null), raf = useRef(null), autoStop = useRef(null);
  const sr = useRef(44100), recording = useRef(false);   // ref = source of truth (autoStop closure can't read stale state)

  const teardown = () => {
    if (raf.current) cancelAnimationFrame(raf.current); raf.current = null;
    clearInterval(timer.current); clearTimeout(autoStop.current); timer.current = autoStop.current = null;
    try { processor.current?.disconnect(); } catch { /* gone */ }
    try { source.current?.disconnect(); } catch { /* gone */ }
    try { analyser.current?.disconnect(); } catch { /* gone */ }
    try { gain.current?.disconnect(); } catch { /* gone */ }
    processor.current = source.current = analyser.current = gain.current = null;
    if (stream.current) { stream.current.getTracks().forEach((t) => t.stop()); stream.current = null; }
    // NB: the shared AudioContext is intentionally left open (see getAudioContext).
  };

  const stop = () => {
    if (!recording.current) return;
    recording.current = false;
    const merged = mergeFloat32(chunks.current);
    setRecording(false);
    teardown();
    if (merged.length < 800) {   // essentially nothing captured — don't send an empty clip
      setError("No audio was captured — check your microphone permission and record again.");
      return;
    }
    setBlob(encodeWav(merged, sr.current));
  };

  const start = async ({ autoStopSeconds = null } = {}) => {
    if (recording.current) return;
    setError(""); setBlob(null); setElapsed(0); chunks.current = [];
    try {
      // AGC / noise-suppression reshape prosody the model reads — and weren't
      // applied to training data. Capture the raw voice instead.
      const s = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false },
      });
      stream.current = s;
      const c = getAudioContext();
      if (c.state === "suspended") { try { await c.resume(); } catch { /* ignore */ } }
      sr.current = c.sampleRate;
      const src = c.createMediaStreamSource(s);
      const an = c.createAnalyser(); an.fftSize = 256;
      const proc = c.createScriptProcessor(4096, 1, 1);
      const g = c.createGain(); g.gain.value = 0;    // silent sink: keeps onaudioprocess firing, no audible feedback
      src.connect(an); an.connect(proc); proc.connect(g); g.connect(c.destination);
      proc.onaudioprocess = (e) => { if (recording.current) chunks.current.push(new Float32Array(e.inputBuffer.getChannelData(0))); };
      source.current = src; analyser.current = an; processor.current = proc; gain.current = g;

      const data = new Uint8Array(an.frequencyBinCount);
      const draw = () => {
        an.getByteFrequencyData(data);
        const size = Math.max(1, Math.floor(data.length / BARS));
        setLevels(Array.from({ length: BARS }, (_, i) => {
          const slc = data.slice(i * size, i * size + size);
          const avg = slc.reduce((a, b) => a + b, 0) / Math.max(1, slc.length);
          return clamp(avg / 255, 0.06, 1);
        }));
        raf.current = requestAnimationFrame(draw);
      };
      draw();
      recording.current = true;
      setRecording(true);
      timer.current = setInterval(() => setElapsed((e) => e + 1), 1000);
      if (autoStopSeconds) autoStop.current = setTimeout(() => stop(), autoStopSeconds * 1000);
    } catch (err) {
      setError(err?.message || "Microphone access failed — check the browser's mic permission.");
      setRecording(false); recording.current = false;
      teardown();
    }
  };

  useEffect(() => () => { teardown(); }, []);
  const reset = () => setBlob(null);
  return { isRecording, elapsed, blob, levels, error, start, stop, reset };
}

// ---------------- speech recognition (transcript, best-effort) ----------------
export function useSpeechRecognition() {
  const [supported] = useState(() =>
    typeof window !== "undefined" && !!(window.SpeechRecognition || window.webkitSpeechRecognition));
  const [listening, setListening] = useState(false);
  const recRef = useRef(null);
  const onTextRef = useRef(null);

  const start = (onText) => {
    if (!supported) return;
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    const rec = new SR();
    rec.continuous = true; rec.interimResults = true; rec.lang = "en-US";
    onTextRef.current = onText;
    let finalText = "";
    rec.onresult = (e) => {
      let interim = "";
      for (let i = e.resultIndex; i < e.results.length; i++) {
        const t = e.results[i][0].transcript;
        if (e.results[i].isFinal) finalText += t + " "; else interim += t;
      }
      onTextRef.current?.((finalText + interim).trim());
    };
    rec.onend = () => setListening(false);
    rec.onerror = () => setListening(false);
    try { rec.start(); setListening(true); recRef.current = rec; } catch { /* already started */ }
  };

  const stop = () => { try { recRef.current?.stop(); } catch { /* noop */ } setListening(false); };
  useEffect(() => () => { try { recRef.current?.stop(); } catch { /* noop */ } }, []);
  return { supported, listening, start, stop };
}
