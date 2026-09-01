# ADR-001: Per-dimension normalized action steps

## Status

Accepted for the adaptive-environment architecture. Individual numerical values
remain unapproved research configuration until the scene profile is approved.

## Context

The original blueprint and initial implementation represented action magnitude
with one normalized `actionStep`. Engineering calibration of the Japanese Temple
Pond Garden found that the smallest noticeable, restrained change differs by
environmental dimension. A single value would make some actions imperceptible
or make other actions unnecessarily large.

The calibrated development values are:

| Dimension | Normalized step |
|---|---:|
| Illumination | 0.10 |
| Color warmth | 0.25 |
| Atmospheric softness | 0.30 |
| Color richness | 0.20 |
| Ambient motion | 0.20 |

## Decision

Each scene parameter profile stores one normalized action step per environmental
dimension. Increase and decrease actions for the same dimension use the same
magnitude. `NoChange` remains a zero-change action.

Policies continue to select from the same eleven discrete actions and do not
receive raw Unity values. The safety validator continues to calculate, clamp and
approve the resulting normalized target before any scene transition occurs.

## Alternatives considered

- One shared normalized step: simpler, but contradicted the scene calibration.
- Raw Unity deltas per action: rejected because it would break the normalized
  policy and scene-mapping boundary.
- Separate increase and decrease magnitudes: deferred because current calibration
  supports symmetric steps and the extra parameters are not yet justified.

## Consequences

- Scene profiles contain five research-sensitive step values instead of one.
- Different scenes can calibrate perceptually comparable action magnitudes.
- Safety limits and total-variation accounting continue to operate in normalized
  space without policy-specific shortcuts.
- Existing serialized profiles require deliberate review rather than silently
  copying one legacy step into all five dimensions.

## Validation plan

- Unit-test validation of every step value and every action-to-dimension mapping.
- Unit-test that each action changes only its intended normalized dimension.
- Run all EditMode and PlayMode suites after migration.
- Validate transitions visually on Quest 2 before approving the research profile.
