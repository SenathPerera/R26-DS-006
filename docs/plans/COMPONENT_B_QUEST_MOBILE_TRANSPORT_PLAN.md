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

**Status:** Implemented and validated in Unity Test Runner. Quest-device and
live Component B endpoint validation remain pending.

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

**Status:** In progress. Transport-neutral messages, strict Quest-side parsing,
the pairing-capable WebSocket transport core, command deduplication, and
outbound Quest-state/visual-telemetry serialization are implemented and covered
by Unity EditMode tests. A runtime Unity bridge now validates the configured
scene on the main thread, forwards relay inputs through `VisualSessionBoundary`,
and publishes session-phase snapshots; its focused PlayMode tests are validated.
The non-secret connection profile and runtime pairing entry point are now
implemented and validated. Locally durable visual telemetry is now exposed to
the bridge and published in configuration-sized relay batches; a failed batch
is retained instead of being discarded. Relay-backend integration, runtime
pairing UI, reconnect credential renewal, final delivery acknowledgement and
offline-log recovery, and production scene wiring remain pending.

- Add a concrete `ISessionTransport` implementation for relay messages.
- Support pairing, configuration/preferences, start/pause/resume/stop/emergency
  commands, readiness/status, and completed visual-log transfer.
- Make command handling idempotent and use stable message IDs.
- Keep session control operational when Component B is temporarily unavailable.

The current draft inbound envelope is intentionally version-configurable; a
production schema identifier has not been invented in code. It contains:

- `schemaVersion`, `messageId`, `messageType`, and `payload`.
- `session_configuration` payloads with `sessionId`,
  `participantPseudonym`, `sceneId`, and five named normalized values under
  `preferredEnvironment`.
- `session_command` payloads with `sessionId` and one of `start`, `pause`,
  `resume`, `stop`, or `emergency_stop`.

For commands, the stable envelope `messageId` is also the coordinator command
ID, preserving idempotency across relay retries. Quest rejects incomplete,
unsupported, non-normalized, or schema-mismatched messages before dispatch.

The current Quest-side transport draft adds these relay messages:

- `pairing_request` from Quest with `pairingCode`, the fixed `quest` client
  role, a pseudonymous Quest installation/client ID, and app version.
- `pairing_result` from the relay with `accepted` and either the bound
  `sessionId` or a non-sensitive `rejectionCode`.
- `quest_state` from Quest with the bound `sessionId`, session phase, and UTC
  timestamp.
- `visual_telemetry_batch` from Quest with the existing versioned visual
  telemetry events and their typed fields.

These are draft cross-component field names until the mobile/relay team freezes
the shared schema. The schema version remains a required runtime input rather
than a hardcoded production identifier. The pairing code is runtime-only and is
never written to a ScriptableObject, scene, diagnostic code, or log message.

The transport reports `Connected` only after the WebSocket is open and the
relay accepts pairing. It rejects messages for a different session and drops
duplicate configuration/command message IDs. Component B physiology remains on
its independent direct stream and is not carried by this transport.

`SessionRelayBridge` is the Unity lifecycle boundary around that transport. Its
PlayMode routing, rejection, and shutdown tests are validated. Transport
callbacks are queued and dispatched on Unity's main thread; scene IDs are
checked against the initialized `ApplicationBootstrap` profile before
preferences reach the production coordinator. Disable/shutdown freezes the
visual network state and disconnects the relay without blocking a frame.

`SessionRelayConnectionProfile` holds only non-secret deployment settings:
relay endpoint, draft schema version, and maximum inbound message size. A
`SessionRelayPairingController` accepts the one-time pairing code and
pseudonymous Quest client ID at runtime, combines them with `Application.version`,
and passes the resulting runtime-only connection object to the bridge. Neither
runtime credential is serialized into the profile or scene. Non-TLS `ws://`
requires an explicit development-only opt-in; deployed configurations should
use `wss://`. The maximum telemetry events per relay batch is also required
deployment configuration because the final relay payload limit has not yet
been agreed with the mobile/relay team.

### Slice E: hardening and Quest validation

**Status:** In progress. The source/configuration baseline and remaining pilot
gates are recorded in
`docs/plans/TEMPLE_POND_QUEST_PILOT_READINESS.md`. The production scene is not
yet wired to the relay because the endpoint, frozen schema, pairing/resume
contract, telemetry limits, and Quest client-ID policy remain cross-team
inputs. No placeholder production values will be serialized into the scene.

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

The current one-time pairing credential is sufficient for the initial socket.
Safe automatic reconnection requires a relay-defined renewable/resume credential
or a fresh code supplied by mobile. That authentication field and lifetime must
be agreed with the relay team before automatic reconnection is enabled; Quest
must not silently reuse or persist a consumed one-time code.

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
- The repository currently contains native Android and React Native mobile
  prototypes; the team must identify the active mobile implementation before a
  matching production relay client is added there.
- `TODO(RESEARCH_DECISION)`: confirm whether the 60-second physiology forwarding
  gate remains in the frozen study configuration after full end-to-end timing
  validation.
