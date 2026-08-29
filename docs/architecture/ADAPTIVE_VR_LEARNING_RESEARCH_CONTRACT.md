# Adaptive VR Learning Research Contract

**Component:** Unity adaptive VR meditation environment  
**Target:** Meta Quest 2 standalone application  
**Status:** Architecture contract; experimental calibration pending  
**Restored:** 2026-08-29

## 1. Purpose and authority

This document records the stable contract between the learning logic, safety
logic, physiological input, session orchestration, environment abstraction,
and research study conditions.

It is a concise implementation contract. It does not approve experimental
thresholds or replace:

- `codex-local/CODEX_BLUEPRINT_ADAPTIVE_VR_MEDITATION.md`
- `codex-local/research_configuration_guidelines_for_codex.md`
- shared cross-component schemas under `contracts/`

If this contract conflicts with the primary blueprint, the blueprint takes
precedence unless an explicit later research decision overrides it. Any such
override must be documented rather than introduced silently in code.

## 2. Learning scope

The MVP learning system is a **safety-constrained contextual bandit**. It is not
full online deep reinforcement learning.

The implementation progression is:

1. Static personalized policy.
2. Rule-based adaptive policy.
3. Contextual-bandit policy, initially disjoint LinUCB.
4. Optional advanced or meta-learning work only after the MVP is stable and
   explicitly approved.

MAML, PEARL, policy-gradient learning, and other deep online learning methods
are outside the MVP contract.

## 3. Study conditions

The comparative study conditions are:

### 3.1 Static Personalized

- Initialize the environment from the approved preference pipeline.
- Schedule and log the same decision opportunities as other conditions.
- Always propose `NoChange`.
- Perform no model updates.

### 3.2 Rule-Based Adaptive

- Initialize from the same preference pipeline.
- Use fixed, predeclared population-level rules.
- Perform no learning.
- Freeze and version the rule set before the main study.

### 3.3 Personalized Contextual Bandit

- Initialize from the same preference pipeline.
- Use the shared normalized observation and action definitions.
- Select conservatively among eligible actions.
- Update only from valid delayed rewards attributed to the selected action.
- Persist participant-specific state only when enabled by approved
  configuration.

All study conditions must use the same session timing, safety validator,
environment transition system, action space, physiological validity rules, and
telemetry infrastructure. A policy must not bypass those systems.

## 4. Environment state contract

The learning layer operates on exactly five normalized dimensions:

1. Illumination.
2. Color warmth.
3. Atmospheric softness.
4. Color richness.
5. Ambient motion.

Each value uses the mathematical domain `[0, 1]`.

For a particular scene, `0` and `1` mean the lowest and highest experimentally
permitted settings in that scene profile. They do not mean arbitrary minimum
and maximum Unity property values.

The policy must never receive or directly manipulate raw values such as:

- light intensity or exposure;
- fog density;
- saturation;
- wind or water-ripple strength;
- particle emission;
- shader or post-processing parameters.

Those values belong to scene-specific mapping profiles and adapters. Forest
Lake and Japanese Temple Pond Garden must expose the same normalized contract
without embedding scene-specific behavior in a policy.

## 5. Action contract

The action space contains exactly eleven actions:

- `NoChange`
- `IncreaseIllumination`
- `DecreaseIllumination`
- `IncreaseWarmth`
- `DecreaseWarmth`
- `IncreaseAtmosphericSoftness`
- `DecreaseAtmosphericSoftness`
- `IncreaseColorRichness`
- `DecreaseColorRichness`
- `IncreaseAmbientMotion`
- `DecreaseAmbientMotion`

An action may change at most one normalized dimension. `NoChange` is a valid,
first-class action and must always remain available as a conservative option.

Action magnitudes must be small, gradual, configured, and approved. The action
enum is fixed; the numerical step size is not yet scientifically approved.

## 6. Required control pipeline

Every proposed action follows this path:

```text
Policy
  -> deterministic safety validator
  -> environment parameter manager
  -> smooth transition
  -> scene adapter/profile
  -> Unity scene objects
```

The policy operates only on abstract normalized state. It must not manipulate
GameObjects, cameras, lights, volumes, particles, materials, water, or other
scene objects directly.

The adaptive visual component does not own adaptive audio. Visual and audio
adaptation must remain experimentally separable.

## 7. Session eligibility contract

New adaptive decisions may occur only during the `Adaptive` phase and only
when all validity and safety gates pass.

- During acclimatization, collect the approved baseline without adaptation.
- During pause, stop decisions and reward attribution while preserving the
  current safe environment.
- Resume only after a valid fresh physiological window is available.
- A pause, emergency stop, or invalid session boundary invalidates an open
  action-response attribution.
- During stabilization, stop exploration and freeze the selected safe state.
- Network loss must preserve the current safe environment and prevent new
  decisions when fresh input is unavailable.
- Emergency exit must remain available without cloud connectivity.

Use monotonic time for decision, transition, settling, timeout, and attribution
sequencing. Use UTC timestamps for cross-component physiological-window
correlation and telemetry.

## 8. Physiological input contract

Component B owns signal processing and stress estimation. Unity consumes its
output and must not fabricate missing physiological values.

Expected physiological information may include:

- source timestamp;
- window start and end timestamps;
- heart rate in BPM;
- RMSSD and SDNN when available and reliable;
- four-level stress classification and probabilities;
- continuous stress score;
- signal quality;
- agreed optional metadata.

### 8.1 Payload association decision

The Component B physiology JSON is **not required** to contain `sessionId` or
`schemaVersion` fields. The active session transport associates an accepted
payload with the current session.

This decision applies only to the Component B physiology payload. Internal
telemetry, policy snapshots, configuration, and other shared contracts should
still carry explicit identity/version metadata where their own schemas require
it.

### 8.2 Validity and reuse

- Reject malformed, out-of-order, duplicate, future-invalid, or otherwise
  invalid windows.
- Do not make decisions from stale or insufficient-quality data.
- Do not calculate rewards from stale or insufficient-quality data.
- Preserve missing optional measurements as missing.
- Do not reuse one post-action window as the reward outcome for multiple
  actions unless a future approved configuration explicitly permits it.
- A post-action window may become the next action's pre-action window when the
  attribution sequence remains valid.

Signal-quality thresholds, freshness limits, baseline requirements, and window
timing remain configurable research decisions.

## 9. Observation and feature contract

Policy observations conceptually contain:

- validated physiology and stress information;
- explicit user preferences;
- current normalized environment state;
- safe default environment state;
- approved session context;
- recent action/outcome history;
- timing information.

All bandit inputs must be finite, bounded, normalized, and constructed by one
versioned feature-vector builder. Feature ordering must not be assembled through
scattered array indexes.

The exact production feature schema remains a research decision. Until it is
frozen, it must remain clearly marked as draft and must not be treated as a
compatible `1.0` research schema.

## 10. Policy interface contract

All policies implement a common boundary equivalent to:

```csharp
public interface IEnvironmentPolicy
{
    string PolicyId { get; }
    string PolicyVersion { get; }

    PolicyDecision SelectAction(PolicyObservation observation);
    void ObserveOutcome(ActionOutcome outcome);
    PolicyStateSnapshot CaptureState();
    void Reset(PolicyResetContext context);
}
```

A policy decision must be reconstructable and should expose, where applicable:

- policy ID and version;
- selected action;
- physiological window sequence;
- decision reason;
- expected reward or score;
- uncertainty;
- exploration status;
- candidate scores;
- feature/model schema information.

An outcome supplied for learning must identify its decision, proposed and
executed actions, valid reward, and pre/post physiological windows. Invalid
rewards must never reach `ObserveOutcome` as model updates.

## 11. Reward contract

Reward measures the response after an environment action. It is not the user's
current stress value.

The configurable MVP form is:

```text
reward =
    stressWeight * normalizedStressImprovement
  + rmssdWeight * normalizedRmssdImprovement
  - heartRateWeight * normalizedHeartRateIncrease
  - changePenaltyWeight * normalizedActionMagnitude
  - discomfortPenalty
  - safetyPenalty
```

The exact weights and normalization method are not approved by this contract.

The attribution sequence is:

```text
valid pre-action physiology window
  -> policy decision
  -> safety validation
  -> environment transition
  -> configured settling interval
  -> valid non-overlapping post-action physiology window
  -> reward calculation
  -> optional model update
```

Do not calculate or learn from a reward:

- during the visual transition or settling interval;
- from a stale, invalid, duplicated, or reused outcome window;
- across a pause, emergency stop, or invalid session boundary;
- across network loss without reliable timing;
- when signal quality is inadequate;
- when the baseline or required measurement is unavailable;
- when action/environment-state correlation is inconsistent.

Invalid data means **no model update**, not a fabricated negative reward.
Every valid reward must expose a complete component breakdown for later
reconstruction.

## 12. Contextual-bandit contract

The first learning implementation is disjoint LinUCB because it is lightweight,
deterministic, interpretable, and suitable for sparse decisions on Quest 2.

At each eligible decision point:

1. Build the versioned normalized context.
2. Generate all eleven actions.
3. Remove actions disallowed by scene limits, sensitivity constraints,
   boundaries, cooldowns, repeated-direction limits, or total-variation limits.
4. Retain `NoChange`.
5. Score eligible candidates.
6. Select with approved conservative exploration and deterministic tie-breaking.
7. Pass the proposal through the independent safety validator.
8. Update only the executed action after a valid delayed reward.

LinUCB numerical operations must:

- use `double` precision;
- validate matrix and feature dimensions;
- use configured ridge regularization;
- guard against singular or non-finite calculations;
- run only at scheduled decision/update points, never per frame;
- be covered by deterministic known-value tests.

Tie-breaking order is:

1. Higher score.
2. Lower action magnitude.
3. `NoChange`.
4. Lower action enum value.

Policy snapshots must be explicitly versioned. Incompatible snapshots must be
rejected rather than silently converted.

## 13. Safety invariants

The learning system must never introduce:

- artificial locomotion;
- policy-controlled camera translation or rotation;
- scene switching during an adaptive session;
- flashing or high-frequency effects;
- sudden exposure or fog changes;
- particle bursts;
- fast near-face objects;
- aggressive full-screen post-processing;
- rapid or high-frequency environmental motion.

Network failure, invalid physiology, policy errors, numerical failures, and
configuration errors must fail safely to the current environment or
`NoChange`. They must not trigger an unvalidated environment change.

## 14. Research configuration and approval

The following are fixed architectural decisions:

| Item | Contract status |
|---|---|
| Five normalized environment dimensions | Fixed |
| Normalized domain `[0, 1]` | Fixed |
| Eleven discrete actions | Fixed |
| One dimension changed per action | Fixed |
| `NoChange` always valid | Fixed |
| Safety validator after every policy proposal | Fixed |
| Smooth environment transitions | Fixed |
| MVP contextual-bandit family | Fixed |
| First bandit implementation: disjoint LinUCB | Fixed |
| Scene-specific raw mappings hidden from policy | Fixed |

The following require explicit configuration, calibration, and approval:

| Item | Contract status |
|---|---|
| Scene-safe raw property ranges | Unapproved |
| Scene-safe default states | Unapproved |
| Preference/default/history blend weights | Unapproved |
| Normalized action step sizes | Unapproved |
| Transition duration | Unapproved |
| Decision interval and cooldown | Unapproved |
| Settling and reward-observation timing | Unapproved |
| Baseline duration, sample count, and normalization | Unapproved |
| Signal-quality and stale-data thresholds | Unapproved |
| Reward weights and penalties | Unapproved |
| Rule-based thresholds and mappings | Unapproved |
| LinUCB exploration coefficient and ridge value | Unapproved |
| Consecutive-direction and total-variation limits | Unapproved |
| Forgetting and cross-session persistence behavior | Unapproved |
| Final feature schema | Unapproved |

Research-sensitive profiles must default to unapproved or deliberately invalid
values. Runtime study configuration must fail closed until the team explicitly
approves the profile. Placeholder values must never silently become study
defaults.

Use `TODO(RESEARCH_DECISION)` at unresolved implementation points.

## 15. Telemetry and reproducibility

Logs must allow reconstruction of:

- study condition, policy ID, and policy version;
- feature/configuration/model schema versions;
- incoming and rejected physiology windows with reason codes;
- session phase and decision opportunity;
- candidate actions and scores where applicable;
- proposed, validated, modified, and executed actions;
- state before and safe target state;
- transition and settling boundaries;
- reward-window opening, matching, invalidation, and closure;
- reward components and total reward;
- model update or skipped-update reason;
- policy snapshot identity and update count;
- pause, resume, network, emergency, and completion events.

Participant identifiers must be pseudonymous. Do not log unnecessary personal
information. Local logging must continue where possible during connectivity
loss.

## 16. Change control

Any change to the following requires an explicit architecture/research review:

- study conditions;
- environment dimensions or action space;
- learning algorithm family;
- feature schema;
- reward definition or attribution rules;
- safety pipeline;
- session timing semantics;
- physiological validity rules;
- model persistence or participant association;
- cross-component payload contracts;
- scene list or automatic scene behavior.

Such changes must update configuration/schema versions, affected tests,
telemetry, and this contract where appropriate.

## 17. Acceptance invariants

The learning component is conformant only when all of the following remain true:

- Policies operate exclusively on normalized abstract state.
- Every proposal passes through safety before environment application.
- Static personalized sessions never adapt the environment.
- Rule-based sessions never update a learning model.
- Contextual-bandit updates occur only after valid attributed rewards.
- Invalid or stale physiology never causes a model update.
- One post-action window is not credited to multiple actions.
- Pause, emergency, and invalid network boundaries cancel attribution.
- `NoChange` remains available and is never treated as an error.
- Research-relevant numerical values are versioned configuration rather than
  scattered constants.
- Scene mappings, policies, reward logic, networking, and telemetry remain
  separate concerns.
- The Quest application stays safe and usable without cloud connectivity.
