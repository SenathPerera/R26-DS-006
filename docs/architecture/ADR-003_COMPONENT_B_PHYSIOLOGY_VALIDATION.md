# ADR-003: Component B physiology validation profile

## Status

Accepted as a provisional pilot configuration. This is an engineering and
project-team approval for runtime use, not evidence that the thresholds are
scientifically optimal.

## Context

Unity must reject malformed, stale, or unsuitable physiology before making a
policy decision or attributing reward. Component B's current implementation
uses 60-beat windows, which are approximately 45 seconds at rest, and performs
inference every five beats. The agreed mobile-to-Quest integration cadence is
one forwarded output every 60 seconds. These are different cadences and must
not be treated as the same setting.

Component B also defines these payload semantics:

- `timestamp` equals `windowEnd`.
- `signalQuality` is the fraction of usable heartbeat data in `[0, 1]`.
- Stress probabilities are rounded and tested with a sum tolerance of `0.005`.

## Provisional pilot values

| Setting | Candidate | Basis |
|---|---:|---|
| Stale after | 90 seconds | Blueprint pilot default; allows 30 seconds of delivery margin beyond the agreed 60-second forwarding cadence |
| Minimum window duration | 30 seconds | Existing Unity development candidate; must be confirmed against the final Component B operating range |
| Maximum future clock skew | 2 seconds | Operational clock-tolerance candidate; requires integration validation |
| Source timestamp tolerance | 0.001 seconds | Component B sets `timestamp` and `windowEnd` from the same endpoint |
| Probability-sum tolerance | 0.005 | Matches Component B parity-test tolerance |
| Decision signal quality | 0.50 | Time-constrained provisional threshold selected to reduce rejected windows while still requiring at least half of the heartbeat data to be usable |
| Reward signal quality | 0.50 | Matches the provisional decision gate; model updates still require all other reward-validity checks |
| Buffered windows | 8 | Operational capacity; sufficient for current decision and reward pipeline |

## Decision

Version the configuration as `component-b-physiology-pilot-v1` and enable its
runtime approval gate using the values above. The `0.50` signal-quality gates
are an explicit time-constrained compromise and must remain configurable.

Low-quality data is not automatically treated as valid: payload structure,
timestamps, freshness, probability consistency, window reuse, transition
exclusion, pause/emergency boundaries, and reward-window rules continue to
apply before a decision or model update.

## Open compatibility point

The 30-second minimum window accepts the expected approximately 45-second
resting window, but Component B windows are beat-count based rather than
fixed-duration. The team must confirm the shortest legitimate window expected
from the supported participant population before the final Step 14 freeze.

## Validation plan

- Confirm the forwarding cadence and clock source across Component B, mobile,
  and Quest.
- Replay representative Component B payloads through Unity validation.
- Verify decision and reward gates at signal-quality boundaries.
- Verify stale, delayed, duplicate, and out-of-order payload handling.
- Revisit and version the signal-quality threshold if integration evidence or
  pilot observations show that `0.50` admits unreliable physiology.
