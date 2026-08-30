# ADR-005: Provisional adaptive VR reward pipeline

## Status

Accepted as a provisional pilot configuration. This approval permits runtime
use for the time-constrained MVP; it does not establish that the reward weights
or normalization parameters are scientifically optimal.

## Context

The production coordinator requires one versioned reward configuration for
baseline normalization, delayed attribution, and model-update weighting. The
session provides 120 seconds of acclimatization, the agreed mobile-to-Quest
physiology cadence is 60 seconds, and Temple Pond transitions take 4.12
seconds.

Requiring three baseline samples could prevent the adaptive phase from
starting when only two forwarded samples arrive during acclimatization.
Reward attribution must also wait long enough for a physiology window that is
fully after the visual transition and settling period.

## Provisional pilot values

| Setting | Candidate | Basis |
|---|---:|---|
| Baseline deviation method | Population | Defined, lightweight initial normalization method |
| Minimum baseline samples | 2 | Minimum supported count; compatible with 120-second acclimatization at a 60-second forwarding cadence |
| Minimum baseline deviation | 0.01 | Numerical floor candidate to avoid unstable normalization |
| Trend window count | 5 | Existing tested development candidate |
| Minimum trend samples | 3 | Existing tested development candidate |
| Settling time | 5 seconds | Blueprint pilot value; exceeds the end of the 4.12-second visual transition before reward observation |
| Maximum attribution wait | 120 seconds | Exceeds the 95-second conservative cadence + minimum-window + settling estimate |
| Stress weight | 1.00 | Blueprint pilot value; primary signal |
| RMSSD weight | 0.35 | Blueprint pilot value; secondary signal |
| Heart-rate weight | 0.15 | Blueprint pilot value; weak penalty for increase |
| Change penalty | 0.10 | Blueprint pilot value; discourages unnecessary movement |
| Discomfort penalty | 2.00 | Blueprint pilot value |
| Safety penalty | 2.00 | Blueprint pilot value |

## Decision

Version the profile as `adaptive-vr-reward-pilot-v1` and enable its runtime
approval gate using the values above. Invalid, stale, reused, or
transition-overlapping physiology continues to produce no model update rather
than a fabricated negative reward.

## Limitations

- The weights are provisional blueprint values, not empirically calibrated
  coefficients.
- Two baseline samples provide limited variance estimation. The configured
  deviation floor reduces numerical instability but does not make that
  baseline scientifically strong.
- The 120-second wait may reduce the number of completed action/reward cycles
  during the 15-minute adaptive phase.

## Validation plan

- Run the existing reward and configuration EditMode suites.
- Confirm the production coordinator compatibility check accepts the timing.
- Run a complete simulated session and inspect skipped/accepted reward events.
- Revisit the weights and baseline count if pilot evidence becomes available.
