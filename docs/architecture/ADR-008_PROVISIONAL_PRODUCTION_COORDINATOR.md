# ADR-008: Provisional production coordinator limits

## Status

Accepted as a provisional pilot configuration. This approval permits runtime
use for the time-constrained MVP; the cadence and safety limits remain
versioned candidates rather than scientifically optimal values.

## Context

The production coordinator must validate that decision timing, physiology
freshness, and delayed reward attribution are compatible with the rate at which
the active transport delivers Component B outputs to Quest. It also owns two
session-level safeguards that constrain otherwise individually safe actions.

Component B may infer internally at a faster beat-based cadence. The active
mobile-to-Quest boundary is expected to forward one output every 60 seconds.

## Provisional pilot values

| Setting | Candidate | Basis |
|---|---:|---|
| Quest-facing physiology interval | 60 seconds | Agreed integration cadence |
| Maximum consecutive same-direction actions | 2 | Blueprint pilot safety value |
| Maximum total normalized variation | 0.90 | Blueprint pilot safety value |

## Compatibility

The candidate configuration satisfies the coordinator's explicit checks:

- Decision interval: `75 >= 60` seconds.
- Physiology stale-after threshold: `90 >= 60` seconds.
- Maximum reward-attribution wait: `120 >= 60` seconds.

The coordinator also emits a conservative timing warning only when attribution
wait is below cadence + minimum physiology window + settling. The configured
wait is `120` seconds and the conservative estimate is `60 + 30 + 5 = 95`
seconds, so no warning is expected.

## Decision

Version the profile as `adaptive-vr-coordinator-pilot-v1` and enable its runtime
approval gate using the values above. The limits complement rather than replace
per-action safety validation, normalized scene limits, cooldowns, phase
restrictions, and transition control.

## Limitations

- The two action limits are provisional blueprint values rather than results
  of participant calibration.
- A decision opportunity is not a guaranteed action. Pending reward,
  insufficient physiology, safety rejection, pause, network loss, or an active
  transition may skip it.
- The effective number of action/reward cycles must be measured from telemetry
  during the full-session validation.

## Validation plan

- Run coordinator configuration and production coordinator tests.
- Initialize the serialized Temple Pond scene with all approved profiles.
- Confirm the coordinator produces no compatibility warning.
- Inspect telemetry for consecutive-action and total-variation enforcement.
