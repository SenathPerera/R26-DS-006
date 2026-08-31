# Component B, Quest, and Mobile Transport Plan

**Status:** Accepted implementation plan for the single-participant pilot

**Last updated:** 2026-08-31

**Scope:** Component B stress ingestion, mobile-to-Quest session coordination,
and visual-session log handoff

## 1. Confirmed decisions

- The pilot runs exactly one participant and one active Component B ingest
  session at a time.
- Component B remains unchanged for this integration. Its existing global
  prediction stream is acceptable only under the single-participant
  deployment constraint.
- The Quest application connects directly to Component B for stress
  predictions. The mobile-to-Quest channel carries session configuration,
  preferences, commands, readiness, and completed VR logs.
- Component B payloads do not gain a Unity-specific `sessionId` or
  `schemaVersion`. The active Quest session owns their session association.
- The mobile and Quest applications should communicate through a
  backend-mediated secure WebSocket relay. Final session-log transfer may use
  HTTPS through the same backend.
- Pairing uses a short-lived, one-time code displayed by the mobile app and
  entered or selected in the Quest app. The relay binds one mobile client and
  one Quest client to the active session and rejects a second active
  participant.
- The mobile application remains responsible for the final Supabase upload.
  The Quest application does not write directly to Supabase.
- Adaptive audio integration is deferred because that component is not yet
  complete. This plan covers the visual adaptive agent only.

## 2. End-to-end pilot flow

1. The mobile app collects long-term onboarding preferences.
2. Before a VR session, the mobile app collects session-specific preferences.
3. The user completes the pre-session voice-companion interaction on mobile;
   its stress result remains a mobile-owned input to session setup.
4. Mobile creates the session, generates a short-lived pairing code, and sends
   the normalized visual preferences and session context to the relay.
5. Mobile prompts the user to put on the headset and launch the Quest app.
6. Quest pairs through the relay, receives the session configuration, validates
   it, and acknowledges readiness.
7. When the session starts, Quest opens the Component B prediction stream and
   associates accepted predictions with its one active session.
8. The visual adaptation pipeline consumes only eligible, fresh Component B
   windows through the existing physiology validation and coordinator flow.
9. At completion or abort, Quest closes Component B connectivity, finalizes its
   local visual-session log, and transfers the log to mobile through the relay.
10. The user completes the post-session voice-companion interaction on mobile.
11. Mobile combines the available mobile results and Quest visual-session log,
    then uploads the complete session record to Supabase.

## 3. Component B interface examined

The current FastAPI service exposes:

| Purpose | Endpoint | Consumer |
|---|---|---|
| Health check | `GET /health` | Deployment/client diagnostics |
| Raw PPG ingest | `WS /ingest` | Mobile |
| Stress prediction stream | `WS /stream` | Quest |
| Latest prediction fallback | `GET /stress/latest` | Quest diagnostics/fallback |
| API documentation | `GET /docs` | Developers |

Local development uses `ws://<component-b-host>:8000/stream`. A deployed build
must use TLS (`wss://`) and must not ship a localhost endpoint.

`/stream` broadcasts future predictions; it does not send the current latest
prediction when a client first connects. The client sends text keepalives.
`/stress/latest` returns `503` until a first prediction exists. If that fallback
is used, Quest must reject any result whose `windowEnd` predates the active
session.

### 3.1 Prediction contract

The parser maps these existing Component B fields without renaming or
reinterpreting them:

- `timestamp`, `windowStart`, and `windowEnd`: POSIX seconds. Component B
  defines `timestamp` as the window endpoint.
- `heartRate`: beats per minute.
- `rmssd` and `sdnn`: milliseconds.
- `signalQuality`: usable heartbeat/RR data quality, not BLE or network quality.
- `stress.mode`: `point` or `band`.
- Point mode uses `stress.level`.
- Band mode uses `stress.level_low` and `stress.level_high`.
- `stress.label`, mode, and levels are authoritative. Unity must not re-derive
  the label from probabilities or continuous score.
- Probability order in Unity is `relaxed`, `mild`, `moderate`, `high`.
- `stress.continuous_score` is retained for policy and reward processing.

The supplied example must be ordinary JSON on the wire: keys use
`level_low`/`level_high` without Markdown escaping, and no Markdown code fences
are part of the payload.

## 4. Cadence and eligibility decision

Component B currently needs a 60-beat initial window (approximately 45
seconds), then may predict every five beats (approximately every four seconds).
That producer cadence is distinct from the adaptive policy decision interval.

For the current pilot, Quest will subscribe continuously but forward at most
the newest eligible prediction once per 60 seconds into the approved production
coordinator. It will deduplicate by `windowEnd`. This preserves the already
tested reward and decision behavior while still receiving enough data to choose
the freshest window.

The configured visual policy decision interval remains 75 seconds. These two
intervals serve different purposes and must remain independently configurable.

## 5. Quest implementation slices

### Slice A: Component B parser

**Status:** Implemented and validated in Unity EditMode on 2026-08-31.

- Add `ComponentBStressPayloadParser` at the networking boundary.
- Map JSON into the existing transport-neutral `PhysiologyWindow` and
  `StressDecision` types.
- Reject empty, malformed, structurally incomplete, or unsupported-mode
  payloads with structured reason codes.
- Leave freshness, units/ranges, probability sums, and signal-quality
  acceptance to `PhysiologyWindowValidator` so validation rules are not
  duplicated.
- Cover point, band, malformed, and incomplete payloads with EditMode tests.

### Slice B: Component B streaming client

**Status:** Implemented in source with focused tests; Unity Test Runner and
Quest-device validation remain pending.

- Implement an asynchronous Quest-compatible WebSocket client behind a
  focused Component B source abstraction.
- Add cancellation, keepalive, reconnect with bounded backoff, and connection
  telemetry without blocking Unity's main thread.
- Keep the current safe environment and stop new decisions when fresh data is
  unavailable.

### Slice C: prediction gate and production wiring

**Status:** Implemented and validated in Unity Test Runner. The Temple Pond
scene is wired to the approved local pilot configuration. Quest-device and
end-to-end Component B validation remain pending.

- Add `windowEnd` deduplication and the configurable 60-second forwarding gate.
- Connect only for an active session and disconnect on completion or abort.
- Forward accepted windows through the existing transport-neutral visual
  session boundary and production coordinator; do not bypass physiology
  validation, reward attribution, policy, or safety layers.

Approved local pilot connection configuration:

- Component B stream: `ws://192.168.1.23:8000/stream`.
- Keepalive interval: 20 seconds.
- Maximum inbound message size: 65,536 bytes.
- Reconnect schedule: eight attempts, one-second initial delay, multiplier 2,
  and a 16-second delay cap. When attempts are exhausted, adaptation remains
  safely frozen rather than fabricating physiological input.

### Slice D: mobile-to-Quest relay adapter

- Add a concrete `ISessionTransport` implementation for relay messages.
- Support pairing, configuration/preferences, start/pause/resume/stop/emergency
  commands, readiness/status, and completed visual-log transfer.
- Make command handling idempotent and use stable message IDs.
- Keep session control operational when Component B is temporarily unavailable.

### Slice E: hardening and Quest validation

- Exercise disconnect/reconnect, stale payload, duplicate payload, second-client
  rejection, session rollover, completion, and abort paths.
- Validate TLS and Android network configuration in a Quest build.
- Run a complete on-device pilot-length session and verify local log recovery.

## 6. Pairing and relay protocol

Recommended pairing sequence:

1. Mobile authenticates the participant and requests a session from the relay.
2. Relay returns a cryptographically random one-time code with a short expiry.
3. Quest submits the code and its app/protocol version over WSS.
4. Relay atomically binds the mobile and Quest connections to that session.
5. Relay rejects expired/reused codes and any second active mobile, Quest, or
   participant binding.
6. Mobile sends the normalized session configuration; Quest validates and
   acknowledges it.
7. Quest publishes readiness and lifecycle state. The user starts locally in
   the headset after the app is ready.
8. Both sides receive explicit disconnect and session-ended state.

The relay is an architectural recommendation, not an authorization to choose a
specific backend framework or hosting provider. Credentials, endpoint URLs,
timeouts, and protocol versions must be deployment configuration, not hardcoded
research constants.

## 7. Session log ownership

Quest owns the authoritative visual-session telemetry produced during the VR
session, including phase changes, physiology acceptance/rejection, policy
decisions, safety results, environment transitions, reward attribution, model
updates, and network state. It should persist locally first and produce a
session-scoped summary/export at completion or abort.

Mobile receives that export, joins it with its pre/post-session data, and owns
the final Supabase upload and retry behavior. Only pseudonymous participant and
session identifiers should cross this boundary.

Audio-agent events are intentionally absent from the first implementation.
When the teammate's audio contract is ready, it should be added as a separately
versioned log contribution rather than coupled to the visual policy.

## 8. Constraints, risks, and follow-ups

- Component B currently has global subscribers, permissive CORS, and no
  session authentication. It is suitable for the accepted controlled
  single-participant pilot, not concurrent or public deployment.
- Supporting concurrent participants later requires Component B session
  scoping or a session-aware broker; the current global stream must not be used
  for that scenario.
- Production endpoints require authentication and TLS. Network loss must never
  fabricate physiology or prevent local emergency exit.
- The exact relay backend, pairing-code lifetime, message schema version, log
  chunking limits, and retention policy remain cross-team decisions.
- `TODO(RESEARCH_DECISION)`: confirm whether the 60-second physiology forwarding
  gate remains in the frozen study configuration after full end-to-end timing
  validation.
