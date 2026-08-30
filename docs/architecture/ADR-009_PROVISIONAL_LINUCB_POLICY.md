# ADR-009: Provisional LinUCB policy configuration

## Status

Accepted as a provisional pilot configuration. This approval permits runtime
use for the time-constrained visual MVP; the hyperparameters remain versioned
candidates rather than scientifically optimal values.

## Context

The production Temple Pond study condition requires a contextual-bandit policy
profile. The implementation uses disjoint LinUCB with one model per action,
double-precision matrix operations, deterministic tie-breaking, and the frozen
policy feature schema. It updates only the executed action after a valid
delayed reward.

No production LinUCB ScriptableObject previously existed, so the serialized
scene could not initialize the contextual-bandit study mode.

## Provisional pilot values

| Setting | Candidate | Meaning |
|---|---:|---|
| Ridge regularization | 1.00 | Blueprint pilot value; initializes each action matrix as the identity and improves numerical conditioning |
| Exploration coefficient | 0.25 | Blueprint pilot value; provides conservative uncertainty-driven exploration |

## Decision

Version the profile as `adaptive-vr-linucb-pilot-v1` and enable its runtime
approval gate using the values above. The profile binds its feature schema
version and dimension from `PolicyFeatureVectorBuilder` at runtime, so a
feature mismatch cannot be silently accepted.

## Safety boundary

LinUCB only ranks actions from normalized observations. Its selection still
passes through candidate restrictions, the final safety validator, session
phase checks, the environment transition manager, and the Temple Pond scene
adapter. Invalid reward produces no model update.

## Audio-agent boundary

This LinUCB instance controls only the five normalized visual-environment
dimensions. Adaptive audio is owned by a separate teammate component and a
separate RL agent. Visual policy observations, actions, model state, and reward
updates must not directly manipulate or absorb audio controls.

If both agents operate during the same session, their action timestamps and
condition identifiers must be logged so research analysis can identify
overlapping interventions. Any shared scheduling, coordinated reward, or joint
policy design is a cross-component research decision and is outside this
profile.

## Limitations

- `1.00` and `0.25` are provisional blueprint values rather than tuned
  participant-response hyperparameters.
- The short 15-minute adaptive phase produces sparse online updates.
- Effective exploration may be lower because unavailable or unsafe actions are
  filtered and pending rewards can skip decision opportunities.

## Validation plan

- Run LinUCB configuration, numerical, deterministic-tie, and policy tests.
- Run the complete action/reward PlayMode cycle.
- Confirm candidate scores, selected action, uncertainty, and valid updates are
  present in JSONL telemetry.
- Confirm pause, emergency, network loss, invalid physiology, and stabilization
  stop new learning updates.
