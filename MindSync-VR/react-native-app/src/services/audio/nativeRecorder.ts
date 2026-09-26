// JS bridge to the native raw-PCM recorder (android/.../audio/AudioRecorderModule.kt).
// Produces a WAV file URI the Component D API layer posts as-is.
//
// Capture policy mirrors the validated pipeline: raw 16-bit PCM mono, no AGC or
// noise-suppression, a minimum listen window, and a silence tail with hysteresis
// keyed to a speech threshold derived from the Layer-1 ambient noise floor.

import {useCallback, useEffect, useRef, useState} from 'react';
import {NativeEventEmitter, NativeModules, PermissionsAndroid, Platform} from 'react-native';

export interface RecorderStartOptions {
  sampleRate?: number;
  minDurationMs?: number;
  silenceTailMs?: number;
  maxDurationMs?: number;
  /** RMS 0..1 below which a frame is silence. <= 0 disables auto-stop (manual stop). */
  silenceThreshold?: number;
}

export interface RecordingResult {
  uri: string;
  durationMs: number;
  sampleRate: number;
}

interface LevelEvent {
  level: number;
  elapsedMs: number;
}

export interface PickedFile {
  uri: string;
  name: string;
  durationMs: number;
  sampleRate: number;
}

interface NativeAudioRecorder {
  start(options: RecorderStartOptions): Promise<boolean>;
  stop(): Promise<RecordingResult>;
  cancel(): Promise<boolean>;
  isRecording(): Promise<boolean>;
  concatWavs(paths: string[]): Promise<RecordingResult>;
  pickAudioFile(): Promise<PickedFile>;
  addListener(eventName: string): void;
  removeListeners(count: number): void;
}

const native = NativeModules.AudioRecorder as NativeAudioRecorder | undefined;

/**
 * DEV demo only: open the system file picker and return a chosen audio file
 * (WAV/MP3/M4A/MP4/…). Returns null if unavailable or the user cancels. The
 * backend decodes non-WAV formats via ffmpeg, so the file is posted as-is.
 */
export async function pickAudioFile(): Promise<PickedFile | null> {
  if (!native?.pickAudioFile) return null;
  try {
    return await native.pickAudioFile();
  } catch {
    return null;
  }
}

/** Join several recorded WAV URIs into one WAV (for the multi-turn capture). */
export async function concatWavs(uris: string[]): Promise<RecordingResult | null> {
  if (!native || uris.length === 0) return null;
  if (uris.length === 1) return {uri: uris[0], durationMs: 0, sampleRate: 16000};
  try {
    return await native.concatWavs(uris);
  } catch {
    return {uri: uris[uris.length - 1], durationMs: 0, sampleRate: 16000};
  }
}

export function isRecorderAvailable(): boolean {
  return Platform.OS === 'android' && !!native;
}

export async function ensureMicPermission(): Promise<boolean> {
  if (Platform.OS !== 'android') return false;
  const granted = await PermissionsAndroid.check(PermissionsAndroid.PERMISSIONS.RECORD_AUDIO);
  if (granted) return true;
  const result = await PermissionsAndroid.request(PermissionsAndroid.PERMISSIONS.RECORD_AUDIO, {
    title: 'Microphone access',
    message: 'The voice companion needs the microphone to listen to your check-in.',
    buttonPositive: 'Allow',
  });
  return result === PermissionsAndroid.RESULTS.GRANTED;
}

const BARS = 40;
const flatLevels = () => Array.from({length: BARS}, () => 0.06);

// NativeEventEmitter types listeners as (...args: Object[]); adapt to a typed payload.
function typedListener<T>(fn: (payload: T) => void) {
  return (...args: unknown[]) => fn(args[0] as T);
}

/**
 * React hook mirroring the web recorder's surface: isRecording, elapsedMs, a
 * rolling waveform (`levels`), any `error`, and start/stop/cancel. When the
 * native side auto-stops (silence tail reached), `result` is populated via the
 * finish event; a manual stop() resolves with the same shape.
 */
export function useRecorder() {
  const [isRecording, setRecording] = useState(false);
  const [elapsedMs, setElapsedMs] = useState(0);
  const [levels, setLevels] = useState<number[]>(flatLevels);
  const [error, setError] = useState('');
  const [result, setResult] = useState<RecordingResult | null>(null);
  const recordingRef = useRef(false);

  useEffect(() => {
    if (!isRecorderAvailable()) return;
    const emitter = new NativeEventEmitter(NativeModules.AudioRecorder);
    const onLevel = emitter.addListener('AudioRecorder.level', typedListener<LevelEvent>(e => {
      setElapsedMs(e.elapsedMs);
      setLevels(prev => {
        const next = prev.slice(1);
        next.push(Math.max(0.06, Math.min(1, e.level)));
        return next;
      });
    }));
    const onFinish = emitter.addListener('AudioRecorder.finish', typedListener<RecordingResult>(r => {
      recordingRef.current = false;
      setRecording(false);
      setResult(r);
    }));
    const onError = emitter.addListener('AudioRecorder.error', typedListener<{message: string}>(e => {
      recordingRef.current = false;
      setRecording(false);
      setError(e.message || 'Recording failed');
    }));
    return () => {
      onLevel.remove();
      onFinish.remove();
      onError.remove();
    };
  }, []);

  const start = useCallback(async (options: RecorderStartOptions = {}) => {
    if (recordingRef.current || !native) return;
    setError('');
    setResult(null);
    setElapsedMs(0);
    setLevels(flatLevels());
    const ok = await ensureMicPermission();
    if (!ok) {
      setError('Microphone permission is needed to record your check-in.');
      return;
    }
    try {
      await native.start(options);
      recordingRef.current = true;
      setRecording(true);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Could not start recording');
    }
  }, []);

  const stop = useCallback(async (): Promise<RecordingResult | null> => {
    if (!recordingRef.current || !native) return null;
    try {
      const r = await native.stop();
      recordingRef.current = false;
      setRecording(false);
      setResult(r);
      return r;
    } catch (e: unknown) {
      recordingRef.current = false;
      setRecording(false);
      setError(e instanceof Error ? e.message : 'Recording failed');
      return null;
    }
  }, []);

  const cancel = useCallback(async () => {
    if (!native) return;
    try {
      await native.cancel();
    } catch {
      /* nothing to cancel */
    }
    recordingRef.current = false;
    setRecording(false);
    setResult(null);
    setLevels(flatLevels());
  }, []);

  const reset = useCallback(() => {
    setResult(null);
    setError('');
    setElapsedMs(0);
    setLevels(flatLevels());
  }, []);

  return {isRecording, elapsedMs, levels, error, result, start, stop, cancel, reset};
}
