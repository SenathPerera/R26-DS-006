# MindSync System Observatory

Read-only localhost dashboard for R26-DS-006. Open **http://127.0.0.1:8090**.
It shows the complete session flow, with parallel paths rather than a misleading
single A-B-C-D-E chain. It never triggers generation, paid capability probes,
model warmup, session commands, or learning updates.

## Run

From the repository root, using Python 3.10 or newer:

```powershell
py -3.12 -m venv .venv-relay # only if the environment does not exist
.\.venv-relay\Scripts\python.exe -m pip install -r services\project_monitor\requirements.txt
.\monitor-project.cmd
```

Start existing backends separately. Defaults: Component B 8000, Component D 8010,
session relay 8080, Lyria 8002. Override probe ports using `MONITOR_B_PORT`,
`MONITOR_D_PORT`, `MONITOR_RELAY_PORT`, `MONITOR_LYRIA_PORT` before launching.
Probes always target 127.0.0.1; this dashboard monitors services on this PC.
Do not bind it to 0.0.0.0 or expose it through a proxy/public tunnel.

## Enable Detailed Reports

1. Restart B, D, Lyria and the session relay with the updated repository code.
2. Rebuild/install the updated mobile app. Its operational heartbeat starts when
   a prepared session connects to the authenticated relay socket, not at app launch.
3. Let Unity recompile, then run the integrated scene and pair the session code.
   `AdaptiveAudioVrSceneInstaller` installs `ProjectMonitorReporter` automatically.
   A headset needs a rebuilt Unity player, not just an editor recompile.
4. Select a session in the dashboard and click a stage for evidence. Do not
   interpret an old heartbeat or reachable HTTP server as a working data path.

## Flow And Evidence

- Preparation: audio preference bundle received by relay; session created.
- A: phone reports BLE connection and telemetry age.
- Phone -> B: mobile frame acknowledgements plus process-wide B receipt time.
- B: model loaded during a real ingest connection, frame/prediction counters.
- D: phone voice-stage/busy/error flags, health/model readiness, inference HTTP
  outcomes and cross-modal B polling outcomes. D counters are process-wide.
  External transcription/LLM/TTS success is not independently verified here.
- Relay -> Unity: authenticated sockets, configuration sent, Unity initialization.
- E generation: Unity reports generation in progress and generated clip loaded.
- Session gate: relay start sent versus Unity phase and audio readiness. The
  dashboard detects but does not fix the existing fixed-delay start race.
- Live adaptation: B windows accepted in Unity, visual policy, audio policy,
  audio running/source-playing/muted flags and audio online-update count.
- Completion: relay visual-log delivery acknowledgement, pending Unity telemetry,
  phone account-sync status. This is not verification of every cloud write or
  audio log upload, and does not prove the music is audible in the headset.

Service counters are process-wide, not participant-attributed. Only client reports
are attached to authenticated sessions. B counters may include another test client.
Voice check-in runs before/after meditation; C and E run in parallel. The local
monitor does not claim that C and E share a single decision loop.

## Live Agents

Open **http://127.0.0.1:8090/#agents**, select the paired session, and use the
**Live agents** tab. Restart the session relay and rebuild the integrated Unity
player for the new reports. No mobile APK, database migration, model retraining,
or firmware update is needed for this view.

- Scene controls: illumination, warmth, atmospheric softness, color richness,
  ambient motion.
- Audio controls, in wire order: intensity, density, brightness, tempo, fade,
  music mix, ambient mix.
- Both show the input stress, confidence, quality and source; current applied
  controls; target controls; change since the previous report; and the last
  decision's proposed versus safety-filtered deltas.
- The coordination strip shows the measurement owner, transition/measurement
  stage, post-window eligibility, deadline, and completed/cancelled turns. Older
  Unity reports remain readable and show coordination as not reported.
- Stress and controls are normalized 0-1. Scene stress is Component B's continuous
  score divided by 3, matching the audio input convention. Controls are not lux,
  BPM, seconds or measured sound levels. Applied audio comes from the mixer,
  not the policy target. Rendering/playback values are not independent proof
  of what the headset user perceives.
- The chart selector covers every variable and overlays that agent's own stress
  input and target. The 5/15-minute range and Live checkbox support inspection.
  Each agent can receive input at a different time and may use a different mode.
  Simulator input is labelled explicitly, never presented as measured stress.
- Decision/outcome history deduplicates retained results. A reward carries its
  own decision/window sequence and event time; it is not assumed to belong to the
  latest action. Visual sequence IDs are physiology windows, audio IDs are local
  decision numbers; they are not comparable between agents. Invalid or absent
  rewards are not shown as zero. Model update counts come from the actual policies.
- These are five-second observations, not a lossless event recorder. Decisions
  or transient values between heartbeats can be missed. Stress co-variation is
  not evidence that an action caused an improvement.

The relay keeps at most 180 snapshots per session and expires observations after
15 minutes, with a maximum of 32 sessions. Charts use server receipt time; event
times come from Unity. Keep the PC and headset clocks synchronized. Missing
samples, input older than 90 seconds (3 seconds for simulation), or disconnected
clients are marked explicitly; plotted lines break across report gaps over 15s.
The last observed decision may predate the plotted range.

When controls stay unchanged, capture three minutes from the Windows repository
root while the wearable and Unity session remain running:

```powershell
.\tools\diagnose-mindsync-windows.ps1 -ObserveSeconds 180 -SampleIntervalSeconds 5 |
  Tee-Object -FilePath .\mindsync-rl-diagnostics.txt
```

The report includes input confidence/quality, held actions, invalid rewards,
coordinator cancellations and post-window timing. It follows the same relay
session throughout. Source limits can differ from Inspector overrides or an
older installed Unity player. A `NoChange`/`Hold stable` action is valid; a
rejected reward does not train either policy. Improve the incoming signal and
inspect the cancellation trace before changing learning or quality settings.

### Responsive RL Testing

To test the real agents with more permissive inputs, stop Play mode, select the
scene object with `AdaptiveAudioVrSceneInstaller`, and enable **Responsive Testing**
under **Testing** in its Inspector. Start a new session. On Quest, enable Unity's
**Development Build** option and rebuild/install the player; this preset is
inactive in ordinary release players. Disable the switch and restart the player
to restore the pilot settings.

| Setting | Pilot Default | Testing |
| --- | --- | --- |
| Visual decision/reward quality minimum | 0.50 | 0.35 |
| Audio input quality minimum | 0.60 | 0.35 |
| Audio input confidence minimum | 0.45 | 0.20 |
| Visual/audio decision opportunity interval | 75 s | 15 s |
| Visual physiology forwarding interval | 60 s | 5 s |
| Visual reward timeout | 120 s | at least 240 s |

Both agents remain LinUCB policies for the ContextualBandit study condition.
Exploration is at least 0.50 in testing. The policies have no minimum stress-delta
trigger, so small changes already enter their context; a change of 0.01 on the
normalized stress scale can contribute to a reward. `NoChange` and `Hold stable`
remain valid selections and visible changes are not guaranteed on each input.
Fifteen seconds is a decision opportunity, not a promise of an action or model
update: warmup, settling and complete non-overlapping reward windows still apply,
and the shared coordinator gives only one agent a measurement turn at a time.

The preset creates runtime configuration copies with `responsive-testing-v1`
identities. Pilot assets and output/action limits stay unchanged; testing model
identities differ from production, and session/audio logs identify the preset.
Live-agent badges gain a `_Testing` suffix and the diagnostic script uses the
testing limits only when the running Unity player reports those badges.

On macOS/Linux, from the repository root:

```sh
python3 -m venv .venv-relay
.venv-relay/bin/python -m pip install -r services/project_monitor/requirements.txt
.venv-relay/bin/python -m uvicorn services.project_monitor.app:app --host 127.0.0.1 --port 8090
```

## Freshness, Privacy And Safety

- Backends are polled every 3 seconds with bounded timeouts; client reports are
  sent every 5 seconds. Heartbeats expire at 15 seconds, BLE evidence at 10 seconds,
  and physiology/frame evidence at 90 seconds (allows a 60-second window).
- Missing diagnostics show **Not reported**, never synthetic healthy data.
  Monitor connection loss dims the historical view and displays a warning.
- Status payloads contain allowlisted flags, counts, modes and timestamps. The
  separate local-only agent feed adds derived stress, confidence/quality,
  control values, actions and rewards. No
  names, participant pseudonyms, pairing codes, tokens, prompts, voice clips or
  raw PPG, HR/HRV or temperature readings are returned. Session labels are
  truncated hashes. Derived stress remains sensitive session data; do not
  publish this dashboard or its screenshots with identifiable participant context.
- Reports use the existing authenticated relay connection. They are not forwarded
  to peers, written into study logs or used as agent input. Invalid diagnostic
  fields are discarded. A maximum of 32 sessions is retained in relay memory.
- Counters reset on backend restart. The browser keeps up to 40 status changes
  in memory only. Reloading the page clears that timeline.
- `/monitor/status` on each backend and `/monitor/agents` on the relay are
  restricted to loopback clients. The UI
  also checks host/origin and has no external assets or external API calls.
- Existing application transports may use plain HTTP/WS on the LAN. This feature
  does not add TLS or repair existing backend authentication limitations.
- Monitoring installation adds lightweight hooks but does not change LinUCB,
  audio policy, reward, generation prompts or start/pause/resume semantics.

## Verification

```powershell
.\.venv-relay\Scripts\python.exe -m pip install httpx==0.28.1
.\.venv-relay\Scripts\python.exe -m unittest services.project_monitor.test_monitor services.session_relay.test_relay_app services.session_relay.test_session_store
```

Agent-specific checks (use your Python environment's executable):

```sh
python -m unittest services.project_monitor.test_agents
node --test services/project_monitor/test_agents.cjs
node services/project_monitor/test_agents_browser.cjs # requires Playwright + Chromium and a running monitor
```

Diagnostic-script checks run without live services, Windows modules or Pester:

```powershell
powershell -NoProfile -File tests\windows_diagnostics\diagnostics.tests.ps1
```

Check mobile TypeScript with `node_modules\.bin\tsc.cmd --noEmit` from the app
folder. `tools/test-audio-online.ps1` compiles the Unity runtime/audio sources
against installed Unity references. Real BLE, phone LAN connectivity, microphone,
headset rendering/audio and successful paid Lyria generation still require a
physical-device session; the monitor cannot replace that verification.
