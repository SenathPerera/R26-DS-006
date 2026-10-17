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

## Freshness, Privacy And Safety

- Backends are polled every 3 seconds with bounded timeouts; client reports are
  sent every 5 seconds. Heartbeats expire at 15 seconds, BLE evidence at 10 seconds,
  and physiology/frame evidence at 90 seconds (allows a 60-second window).
- Missing diagnostics show **Not reported**, never synthetic healthy data.
  Monitor connection loss dims the historical view and displays a warning.
- Status payloads contain allowlisted flags, counts, modes and timestamps. No
  names, participant pseudonyms, pairing codes, tokens, prompts, voice clips or
  physiology readings are returned. Session labels are truncated hashes.
- Reports use the existing authenticated relay connection. They are not forwarded
  to peers, written into study logs or used as agent input. Invalid diagnostic
  fields are discarded. A maximum of 32 sessions is retained in relay memory.
- Counters reset on backend restart. The browser keeps up to 40 status changes
  in memory only. Reloading the page clears that timeline.
- `/monitor/status` on each backend is restricted to loopback clients. The UI
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

Check mobile TypeScript with `node_modules\.bin\tsc.cmd --noEmit` from the app
folder. `tools/test-audio-online.ps1` compiles the Unity runtime/audio sources
against installed Unity references. Real BLE, phone LAN connectivity, microphone,
headset rendering/audio and successful paid Lyria generation still require a
physical-device session; the monitor cannot replace that verification.
