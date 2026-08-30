# ADR-003: Component B physiology validation profile

## Status

Proposed for development review. The Unity profile remains deliberately
unapproved and cannot be used by the production coordinator until the values
below are explicitly accepted.

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

## Proposed development values

| Setting | Candidate | Basis |
|---|---:|---|
| Stale after | 90 seconds | Blueprint pilot default; allows 30 seconds of delivery margin beyond the agreed 60-second forwarding cadence |
| Minimum window duration | 30 seconds | Existing Unity development candidate; must be confirmed against the final Component B operating range |
| Maximum future clock skew | 2 seconds | Operational clock-tolerance candidate; requires integration validation |
| Source timestamp tolerance | 0.001 seconds | Component B sets `timestamp` and `windowEnd` from the same endpoint |
| Probability-sum tolerance | 0.005 | Matches Component B parity-test tolerance |
| Decision signal quality | 0.80 | Blueprint pilot default; research approval required |
| Reward signal quality | 0.80 | Blueprint pilot default; research approval required |
| Buffered windows | 8 | Operational capacity; sufficient for current decision and reward pipeline |

## Decision

Create `ComponentBPhysiologyValidationProfile` with the proposed values, but
leave `Research Configuration Approved` disabled. This makes the candidate
configuration visible and reviewable without silently treating it as an
approved pilot configuration.

## Open compatibility point

The 30-second minimum window accepts the expected approximately 45-second
resting window, but Component B windows are beat-count based rather than
fixed-duration. The team must confirm the shortest legitimate window expected
from the supported participant population before approval.

## Validation plan

- Confirm the forwarding cadence and clock source across Component B, mobile,
  and Quest.
- Replay representative Component B payloads through Unity validation.
- Verify decision and reward gates at signal-quality boundaries.
- Verify stale, delayed, duplicate, and out-of-order payload handling.
- Approve and version the asset only after these compatibility checks pass.
