# ADR-018: Production networking hardening

## Status

Proposed and implemented for review on 2026-10-11.

## Context

The `mindsync-session-v3` relay path paired and transferred data correctly
during an uninterrupted connection, but three failure modes remained:

- Quest did not automatically reconnect after relay loss;
- the relay's one-time pairing rule also rejected the already-bound Quest;
- replaying a telemetry batch after a lost acknowledgement created a new
  message ID and could duplicate the durable visual log.

The wire payloads are already frozen by ADR-014. Hardening must therefore
preserve schema v3, keep emergency stop local, and avoid changing policy,
reward, or session timing.

## Decision

The session relay uses the already approved Quest reconnect profile
`component-b-quest-reconnect-pilot-v1` / `1`: eight attempts, one-second
initial delay, sixteen-second maximum delay, and a multiplier of two. The
profile name is historical; its frozen values now govern both Quest network
connections. No new reconnect timing was introduced.

Each WebSocket connection-and-pairing attempt has a configurable 20-second
limit, matching the relay's existing pairing-handshake timeout. This is an
operational deployment limit, not an adaptive-study parameter. The development
relay profile is versioned as `temple-pond-session-relay-dev-v2` / `2`.

After an already-paired connection is lost, and across subsequent failed
reattachment attempts:

- the existing visual-session boundary receives `Disconnected`, preserving
  the current safe environment and preventing fresh adaptive work;
- reconnection runs asynchronously with the bounded approved schedule;
- a successful reconnect republishes the current Quest phase;
- locally durable telemetry remains queued; and
- exhausted retries leave the session safely disconnected with the diagnostic
  code `relay-reconnect-exhausted`.

The prepared session continues to bind the first successful
`questClientId`. The same pairing code and same Quest client ID may reattach
while that session is active, including after the code's first-use expiry.
A different Quest client ID and every ended session remain rejected. A new
authenticated Quest socket replaces a stale socket for the same session.

An unacknowledged telemetry batch retains its original relay `messageId`
across retries. The relay accepts each Quest message ID once per active
session, while sending another delivery acknowledgement for a duplicate
telemetry message. Therefore an acknowledgement lost in transit does not
duplicate the durable log or mobile forwarding.

## Compatibility and limits

- `mindsync-session-v3` is unchanged; no producer or consumer payload fields
  were added.
- Mobile commands sent while Quest is disconnected are rejected as
  `quest-not-connected`; mobile must retry an intentional command after the
  Quest reconnects.
- Duplicate suppression is scoped to the in-memory prepared session. The
  current single-participant relay does not recover active sessions across a
  relay process restart.
- Production deployment still requires HTTPS/WSS and deployment
  authentication. The committed `ws://` override remains development-only.

## Consequences

- Temporary relay loss no longer requires the participant to enter a second
  pairing code.
- Telemetry delivery is idempotent for retry after ambiguous delivery.
- Reconnect work and delays remain off Unity's main thread.
- Local emergency stop and safe-environment rendering remain independent of
  relay availability.

## Validation

- Build the Unity runtime, EditMode-test, and PlayMode-test assemblies.
- Run relay store and integration tests, including same-Quest reattachment and
  duplicate telemetry acknowledgement.
- Run Unity tests covering retained message IDs and automatic bridge
  reconnect.
- On Quest 2, disconnect and restore the relay during `Adaptive`; verify the
  environment freezes, reconnects without a new code, republishes the current
  phase, and delivers each telemetry event once.

