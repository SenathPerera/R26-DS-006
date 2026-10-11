# ADR-015: Production reward-input availability

## Status

Accepted and frozen for `temple-pond-pilot-freeze-v1` by ADR-016.

## Context

The production coordinator previously passed literal zeroes for discomfort and
safety severity into every reward calculation. The safety validator already
records whether an accepted action was modified, so the safety value was
available but unused. There is no action-attributed, real-time discomfort input
in the approved mobile-to-Quest contract.

Treating a missing discomfort report as an observed zero makes the reward look
more complete than the evidence supports. Adding a prompt after every action
would conflict with the blueprint's MVP subjective-feedback protocol. The
post-session comfort questionnaire is too late to attribute to one action and
belongs in offline evaluation.

## Decision

- Replace the coordinator's raw penalty numbers with a typed reward-penalty
  observation created from the accepted safety-validation result.
- Define MVP safety severity as binary and reconstructable: `1` when the safety
  layer modifies an accepted action and `0` when it accepts the action without
  modification. Rejected actions do not open a reward window.
- Preserve missing discomfort as `null`. If a future configuration assigns a
  positive discomfort weight without supplying an observation, reject that
  reward and skip the model update.
- Version the production reward profile as
  `adaptive-vr-reward-pilot-v2`, with the online discomfort weight disabled.
  Keep the provisional safety weight enabled because its input now comes from
  the safety validator.
- Log input availability, severities, intervention status, and validation
  reason with every calculated reward.
- Keep emergency-stop and pause behavior unchanged: they invalidate an open
  attribution instead of fabricating a penalty-bearing reward.

## Alternatives considered

- Interpret no report as zero discomfort. Rejected because missing evidence is
  not an observation.
- Infer discomfort from physiology, pause, pre-session headache/eye strain, or
  emergency stop. Rejected because those signals do not establish discomfort
  caused by one environment action.
- Prompt for comfort after each action. Rejected for the MVP because it would
  interrupt meditation and change the approved study protocol.
- Remove the safety term. Rejected because the safety validator already
  provides a deterministic action-level intervention signal.

## Consequences

- Production rewards no longer contain two unconditional placeholder zeroes.
- Safety-modified actions receive the configured safety penalty and are
  excluded from stabilization candidates.
- The online discomfort component remains disabled until the study protocol
  approves a time-aligned input. Post-session comfort remains available for
  offline analysis.
- The binary safety mapping and reward weights are frozen for the engineering
  pilot and must be versioned if pilot evidence justifies a revision.

## Validation plan

- Unit-test missing versus observed discomfort handling.
- Unit-test safety severity from clipped and unmodified accepted actions.
- Verify reward telemetry distinguishes missing discomfort from zero.
- Run the Unity EditMode and PlayMode suites.
- In a pilot log, confirm ordinary accepted actions record safety severity `0`
  and clipped accepted actions record safety severity `1`.
