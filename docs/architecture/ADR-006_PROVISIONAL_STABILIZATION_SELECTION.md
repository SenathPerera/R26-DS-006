# ADR-006: Provisional stabilization-state selection

## Status

Proposed for time-constrained pilot review. The serialized profile remains
unapproved until the project team explicitly accepts these values.

## Context

The final stabilization phase must stop exploration and freeze one safe state.
The implemented selector evaluates only recent valid action outcomes, excludes
states associated with discomfort or a safety concern, rejects states outside
the scene limits, and falls back to the safe preference-initialized state when
no candidate remains.

The blueprint suggests considering the last three to five valid outcomes,
weighting reward by recency, and penalizing distance from the explicit user
preference.

## Proposed pilot values

| Setting | Candidate | Meaning |
|---|---:|---|
| Recent outcome count | 4 | Middle of the blueprint's suggested three-to-five range |
| Reward recency decay | 0.80 | Each step into the past retains 80% of the following outcome's reward contribution |
| Preference-distance penalty | 0.25 | Moderately favors states close to the user's initialized preference |

For four retained outcomes, the reward multipliers from newest to oldest are
`1.0`, `0.8`, `0.64`, and `0.512` before applying the preference penalty.

## Decision

Create `AdaptiveVrStabilizationSelectionProfile` with the candidate values and
leave `Research Configuration Approved` disabled. The fallback, exclusions,
and normalized scene-limit checks remain deterministic and are not bypassed by
this configuration.

## Limitations

- The decay and preference penalty are tested engineering candidates, not
  empirically calibrated participant-response parameters.
- Fewer than four valid outcomes may exist because invalid rewards and skipped
  decisions are intentionally excluded. The selector can operate on the valid
  subset and falls back when none exist.
- With a short session and delayed reward attribution, this configuration may
  represent substantially fewer than four environment changes.

## Validation plan

- Run stabilization configuration and selector EditMode tests.
- Verify excluded outcomes and preference fallback in telemetry.
- Confirm stabilization stops new exploration and freezes the selected state.
- Revisit the values if pilot evidence becomes available.
