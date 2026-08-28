# Component D — Manual Device Test (Voice Companion)

Run this end-to-end on a real Android device (Samsung) after the backend is warm.
Repo root on this machine: `/Users/prathikesh/Projects/R26-DS-006`.

## Bring-up

```bash
# terminal 1 — companion LLM
ollama serve

# terminal 2 — Component D
cd /Users/prathikesh/Projects/R26-DS-006/component-d
lsof -ti :8010 | xargs kill -9
OLLAMA_MODEL=qwen2.5:3b .venv/bin/uvicorn server.main:app --host 0.0.0.0 --port 8010
# WARM THE ENCODER before touching the phone (first /infer loads ~1.8 GB):
curl -s -F "file=@sample.wav" "http://localhost:8010/infer?session_id=warmup&phase=pre" | head -c 200

# terminal 3 — optional, for real Layer 4
.venv/bin/python scripts/fake_b_server.py       # or Senath's real component-b on :8000
#   drive it:  curl -X POST 'http://127.0.0.1:8000/_ready?ready=true'
#              curl -X POST 'http://127.0.0.1:8000/_set?level=moderate&confidence=0.82'

# terminal 4 — Android
export ANDROID_HOME=/opt/homebrew/share/android-commandlinetools
export JAVA_HOME=/opt/homebrew/opt/openjdk@17
export PATH="$ANDROID_HOME/platform-tools:$PATH"
adb devices
adb reverse tcp:8010 tcp:8010                    # required after EVERY reconnect
adb reverse tcp:8000 tcp:8000                    # only if testing real Layer 4
cd ../MindSync-VR && ./gradlew installDebug
adb shell am start -n com.mindsyncvr/.MainActivity
adb logcat -s MindSyncVoice:V
```

Pull a captured clip to settle "capture vs transport" (debug builds dump every WAV):

```bash
adb exec-out run-as com.mindsyncvr ls files/voice_debug
adb exec-out run-as com.mindsyncvr cat files/voice_debug/<name>.wav > pulled.wav
curl -s -F "file=@pulled.wav" "http://localhost:8010/infer?session_id=pulled&phase=pre" | head -c 300
```

If `pulled.wav` scores correctly via curl but the in-app read failed, the bug is
transport. If the WAV itself is bad, it's capture.

## Pass/fail checklist

- [ ] Mic permission prompt appears once with a rationale; denial is handled, no crash.
- [ ] Intro step asks my name + language; companion greets me by that name out loud (BUG-5, BUG-7).
- [ ] Bot avatar renders and visibly changes across idle / speaking / listening / thinking.
- [ ] The mic does **not** open until the companion has finished speaking (BUG-1).
- [ ] Ambient check listens a full ~8 seconds with a visible countdown.
- [ ] Noisy room → **fails**, the companion speaks a specific suggestion, no skip possible (BUG-4).
- [ ] Quiet room → passes and advances automatically.
- [ ] Pre conversation: no record button; **my transcribed words appear on screen** (BUG-2).
- [ ] The companion's reply clearly responds to what I actually said, not a template.
- [ ] Staying silent triggers escalating, natural follow-ups — different each time.
- [ ] Speaking across several short turns accumulates and eventually scores (BUG-3).
- [ ] `adb pull` a Layer-2 debug WAV → it's **my** voice, clean, no TTS bleed → same file
      scores the same via curl.
- [ ] VR hand-off state appears; I can resume into the post conversation.
- [ ] Post conversation behaves identically to pre.
- [ ] Report shows all five layers honestly (Layer 5 present now that anomaly_v2.pt exists).
- [ ] With Component B running, Layer 4 is real, not mock (BUG-6); live body reading shows.
- [ ] With B stopped, Layer 4 says "wasn't available" rather than fabricated agreement.
- [ ] Debug "mock wristband" toggle forces Layer 4 for demos (debug builds only).
- [ ] Killing the backend mid-flow gives a clear error state — never a crash or silent hang.
- [ ] Forgetting `adb reverse` gives "can't reach the check-in service", not a frozen spinner.
- [ ] A crisis phrase → calm supportive response, scoring stops, support information shown.

## Tuning knobs (adjust on real hardware)

- `VoiceRecorder.SPEECH_RMS = 0.018` — energy VAD threshold (when a turn ends).
- `CaptureParams.SILENCE_TAIL_SEC = 1.8` — pause that ends a turn.
- `CaptureParams.TARGET_SPEECH_SEC = 12` — cumulative speech budget before scoring.
- `CaptureParams.NO_SPEECH_TIMEOUT_SEC = 7` — when a silent turn gives up.
- `SETTLE_MS = 350` (VoiceCheckInScreen) — pause after TTS before the mic opens.
