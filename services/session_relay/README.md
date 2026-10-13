# MindSync Session Relay

Development relay for the controlled single-participant mobile/Quest pilot.
It creates one prepared session, issues a six-digit initial-pairing code, binds
one Quest identity, delivers the Temple Pond configuration and start command,
persists Quest messages to JSON Lines, and forwards them to the authenticated
mobile connection.

If that Quest loses its WebSocket, the same `questClientId` may reattach to the
active session with the original code; another Quest remains rejected. A
replayed telemetry message receives another acknowledgement but is not
appended or forwarded twice.

The session creation request must include one exact `studyCondition` value:
`StaticPersonalized`, `RuleBasedAdaptive`, or `ContextualBandit`. The relay
stores that assignment and forwards it unchanged under the
`mindsync-session-v3` schema.

Session creation also requires a bounded `mindsync-session-context-v1`
snapshot. The relay forwards the validated snapshot for Quest telemetry; it
does not derive preferences or policy features from it. The shared request and
Quest configuration shape is defined in
`contracts/session/mindsync-session-v3.schema.json`.

The relay does not start a paired session immediately. It waits for Quest to
publish its validated `ready` phase, then observes a 30-second initialization
delay before sending the single `start` command.

Mobile also sends `audioPreferences` in `POST /sessions`: schema version
`mindsync-audio-preferences-v1`, a complete `longTerm` audio profile,
`preferenceMode` (`usual` or `adjust`), and a `sessionOverride` profile only
for `adjust`. The relay validates and forwards the bundle in
`session_configuration` alongside the stable participant pseudonym and scene.
Audio on Quest waits for that bundle before preparing its model or requesting
Lyria music. Older clients without the optional field can still use the
visual relay, but cannot start integrated personalized audio.

When Quest publishes `completed` or `aborted`, the relay finalizes that
session's append-only visual log. Mobile downloads the snapshot from
`GET /sessions/{sessionId}/visual-log` and confirms the exact message count and
last message ID through
`POST /sessions/{sessionId}/visual-log/acknowledgement`. Repeating the same
acknowledgement is safe; a stale or mismatched snapshot is rejected.

## Run

From the repository root:

```bash
python -m venv .venv-relay
.venv-relay/Scripts/python -m pip install -r services/session_relay/requirements.txt
.venv-relay/Scripts/python -m uvicorn services.session_relay.app:app --host 0.0.0.0 --port 8080
```

Unity derives the local relay endpoint from `MINDSYNC_DEVELOPMENT_HOST` in
`services/lyria_backend/.env`. Change that one value when the development
machine address changes. A deployed pilot must put this service behind
HTTPS/WSS and authentication.

The default access-code lifetime is five minutes. Override it through
`SESSION_RELAY_CODE_LIFETIME_SECONDS`. Quest messages are durably appended
under `services/session_relay/data/` by default; override that location with
`SESSION_RELAY_DATA_DIR`.

The default readiness-to-start delay is 30 seconds. Override it through
`SESSION_RELAY_INITIALIZATION_DELAY_SECONDS`; use `0` only for automated tests
or deliberate development diagnostics.

## Test

```bash
python -m pip install -r services/session_relay/requirements-dev.txt
python -m unittest services.session_relay.test_session_store services.session_relay.test_relay_app
```

The integration suite covers the prepared-session HTTP endpoint, authenticated
mobile WebSocket, initial Quest pairing, configuration and start delivery,
readiness-gated initialization, same-Quest reattachment, mobile command
forwarding, idempotent Quest telemetry acknowledgement/forwarding, terminal
log finalization, idempotent mobile delivery acknowledgement, and durable
visual-log recovery.
