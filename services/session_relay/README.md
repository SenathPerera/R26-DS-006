# MindSync Session Relay

Development relay for the controlled single-participant mobile/Quest pilot.
It creates one prepared session, issues a six-digit one-time code, binds one
Quest WebSocket, delivers the Temple Pond configuration and start command,
persists Quest messages to JSON Lines, and forwards them to the authenticated
mobile connection.

The relay does not start a paired session immediately. It waits for Quest to
publish its validated `ready` phase, then observes a 30-second initialization
delay before sending the single `start` command.

## Run

From the repository root:

```bash
python -m venv .venv-relay
.venv-relay/Scripts/python -m pip install -r services/session_relay/requirements.txt
.venv-relay/Scripts/python -m uvicorn services.session_relay.app:app --host 0.0.0.0 --port 8080
```

Use `http://172.20.10.4:8080` from local devices on the current development
network. Change the host when the development machine address changes. A
deployed pilot must put this service behind HTTPS/WSS and authentication.

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
mobile WebSocket, one-time Quest pairing, configuration and start delivery,
readiness-gated initialization, mobile command forwarding, Quest telemetry
acknowledgement/forwarding, and durable visual-log recovery.
