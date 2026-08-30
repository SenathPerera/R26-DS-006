# ADR-002: Temple Pond development session timing

## Status

Accepted for development validation; the pilot configuration remains subject
to Step 14 freeze and Quest 2 validation.

## Context

The experience reserves 20 minutes from initialization through stabilization.
Component B currently produces a physiology output every 60 seconds. Scheduling
decisions at exactly the same cadence would leave no margin for transport and
processing jitter and would not guarantee that a valid post-transition reward
window is available.

The session state machine times acclimatization, adaptation, and stabilization.
Scene loading and safety initialization occur before the `Start` command and
therefore are not included in `SessionTimingConfiguration`.

## Decision

- Reserve 30 seconds for initialization, scene readiness, configuration, and
  safety checks before the session `Start` command.
- Configure 120 seconds of acclimatization.
- Configure 900 seconds of adaptive operation.
- Configure 150 seconds of stabilization.
- Schedule decision opportunities every 75 seconds during the adaptive phase.
- Treat an opportunity as a validity check, not a guaranteed environment
  action. Pending reward attribution, stale or missing physiology, an active
  transition, network loss, pause, or safety rejection may skip it.

The timed phases total 1,170 seconds. Together with the separately owned
30-second initialization period, the planned experience totals 1,200 seconds.

## Alternatives considered

- A 60-second interval matching Component B output cadence was rejected for
  this development configuration because it provides no scheduling margin and
  may increase skipped opportunities.
- A 1,200-second timed profile plus 30-second initialization was rejected
  because it would make the participant experience approximately 20 minutes
  30 seconds rather than the selected 20 minutes.

## Consequences

- Approximately eleven decision opportunities occur during the 15-minute
  adaptive phase; the effective action count may be lower.
- The mobile/transport boundary must delay `Start` until the 30-second
  initialization and readiness gate has completed.
- Reward-attribution timeout and physiology-window rules still require an
  independently approved configuration compatible with Component B's
  60-second output cadence.

## Validation plan

- Run session-state EditMode tests against the configured phase durations and
  decision interval.
- Run the production coordinator PlayMode suite.
- Verify the complete phase timeline in JSONL telemetry.
- Validate the full 20-minute protocol on Quest 2 before Step 14 freeze.
