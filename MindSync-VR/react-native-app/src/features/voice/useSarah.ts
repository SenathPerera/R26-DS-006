// Sarah's voice: plays the companion line (ElevenLabs via the native player,
// on-device TTS fallback) AND reveals her words on screen in time with the
// audio — text appears AS she speaks it, never before (§0, §2, §4).

import {useCallback, useEffect, useRef, useState} from 'react';
import {NativeEventEmitter, NativeModules, Platform} from 'react-native';
import {componentDService} from '../../services/api/componentDService';

interface NativeAudioPlayer {
  speak(url: string | null, text: string, language: string | null): Promise<{source: string; durationMs: number}>;
  stop(): Promise<boolean>;
  addListener(e: string): void;
  removeListeners(n: number): void;
}
const player = NativeModules.AudioPlayer as NativeAudioPlayer | undefined;

export function useSarah() {
  const [visibleText, setVisibleText] = useState('');
  const [speaking, setSpeaking] = useState(false);
  const timer = useRef<ReturnType<typeof setInterval> | null>(null);
  const pending = useRef('');
  const revealedRef = useRef(false);

  const clearReveal = () => {
    if (timer.current) clearInterval(timer.current);
    timer.current = null;
  };

  const reveal = useCallback((text: string, durationMs: number) => {
    clearReveal();
    revealedRef.current = true;
    const words = text.split(' ');
    const per = Math.max(90, Math.min(320, durationMs > 0 ? durationMs / Math.max(1, words.length) : 260));
    let i = 0;
    setVisibleText('');
    timer.current = setInterval(() => {
      i += 1;
      setVisibleText(words.slice(0, i).join(' '));
      if (i >= words.length) clearReveal();
    }, per);
  }, []);

  // Real duration arrives when playback begins → sync the word reveal to it.
  useEffect(() => {
    if (!player) return;
    const emitter = new NativeEventEmitter(NativeModules.AudioPlayer);
    const sub = emitter.addListener('AudioPlayer.start', (...args: unknown[]) => {
      const e = args[0] as {durationMs?: number};
      if (pending.current) reveal(pending.current, e?.durationMs ?? 0);
    });
    return () => sub.remove();
  }, [reveal]);

  const say = useCallback(async (text: string, language?: string): Promise<void> => {
    if (!text) return;
    pending.current = text;
    revealedRef.current = false;
    setSpeaking(true);
    const words = text.split(' ').length;
    const estMs = Math.max(1500, words * 320 + 800);
    try {
      if (Platform.OS === 'android' && player) {
        const url = componentDService.ttsUrl(text, language);
        // SAFETY NET: if the native player never resolves — e.g. the ElevenLabs
        // proxy is down and MediaPlayer stalls on a non-audio 503, or on-device
        // TTS has no engine — resolve anyway after a generous timeout so the
        // conversation NEVER gets stuck waiting for Sarah to "finish speaking".
        // If start never fired, reveal the line on the estimate so it still shows.
        await new Promise<void>(resolve => {
          let done = false;
          const finish = () => { if (!done) { done = true; resolve(); } };
          const timer = setTimeout(() => { void player.stop().catch(() => {}); finish(); }, estMs + 5000);
          const revealTimer = setTimeout(() => { if (!revealedRef.current) reveal(text, estMs); }, 1200);
          player.speak(url, text, language ?? null)
            .then(() => { clearTimeout(timer); clearTimeout(revealTimer); finish(); })
            .catch(() => { clearTimeout(timer); clearTimeout(revealTimer); finish(); });
        });
      } else {
        reveal(text, words * 260); // no native player: calm-paced text only
        await new Promise<void>(r => setTimeout(() => r(), words * 260 + 300));
      }
    } catch {
      /* fall through */
    } finally {
      clearReveal();
      setVisibleText(text);
      setSpeaking(false);
    }
  }, [reveal]);

  const stop = useCallback(() => {
    clearReveal();
    setSpeaking(false);
    void player?.stop().catch(() => {});
  }, []);

  useEffect(() => () => clearReveal(), []);

  return {visibleText, speaking, say, stop};
}
