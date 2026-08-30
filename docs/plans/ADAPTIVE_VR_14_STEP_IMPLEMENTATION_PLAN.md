# Adaptive VR — Original 14-Step Implementation Plan

**Status:** Historical implementation baseline  
**Scope:** Unity adaptive VR environment and learning component

> This document preserves the original agreed implementation sequence. Later
> explicit research or architecture decisions take precedence where they
> conflict with this plan. In particular, Component B physiology payloads are
> no longer required to embed `sessionId` or `schemaVersion`; the active
> transport owns session association. ADR-004 also narrows the MVP study to the
> Japanese Temple Pond Garden; Forest Lake is no longer planned or required for
> completion.

## 1. Freeze the RL-facing research contract

Before coding, document and version the inputs and configurable decisions:

- Approved physiological payload fields and schema version.
- Feature schema and ordering.
- Handling of missing RMSSD or other optional measurements.
- Signal-quality and staleness rules.
- Reward components and normalization method.
- Decision, transition, settling, and reward-window timing.
- LinUCB alpha, ridge regularization, persistence behavior, and reset rules.
- Candidate restrictions and total-variation limits.
- Stabilization-state selection rule.

Unfinalized values remain configuration fields marked
`TODO(RESEARCH_DECISION)`. The example values in the blueprint must not
silently become study constants.

No changes to `component-b/` or `component-d/` should be made. Any
shared-contract change must first be agreed across components.

## 2. Establish Unity code and assembly boundaries

Create the blueprint structure under `Assets/Laminar VR/`:

```text
Scripts/Contracts
Scripts/Environment
Scripts/Sessions
Scripts/Physiology
Scripts/Rewards
Scripts/Safety
Scripts/Policies/Static
Scripts/Policies/RuleBased
Scripts/Policies/ContextualBandit
Scripts/Telemetry
Scripts/Application
Tests/EditMode
Tests/PlayMode
```

Introduce focused runtime and test assembly definitions. Keep pure policy,
statistics, reward, and validation code independent of `MonoBehaviour`
wherever practical.

**Gate:** A minimal EditMode test assembly compiles and runs.

## 3. Build the environment domain foundation

Implement:

- `EnvironmentState`
- `EnvironmentAction`
- Action application
- Normalized clamping
- Scene-range clamping
- State distance
- `SceneParameterProfile`
- `IEnvironmentParameterManager`
- `ISceneEnvironmentAdapter`

Add EditMode tests covering all eleven actions, including `NoChange`,
one-dimension-only changes, bounds, and total variation.

**Gate:** Every action produces a safe normalized target without accessing
Unity scene objects.

## 4. Implement the safety pipeline

Implement:

- `IActionSafetyValidator`
- `ActionValidationResult`
- Structured reason codes
- Boundary and cooldown checks
- Sensitivity restrictions
- Consecutive-direction limits
- Total-variation limits
- Invalid/stale physiology restrictions
- Transition, pause, stabilization, and emergency restrictions

Candidate filtering may remove clearly unavailable actions, but the selected
action must still pass through the final safety validator.

**Gate:** No proposed action can reach the environment manager without
validation.

## 5. Implement the session state machine and local simulator

Implement the required phases:

- `Boot`
- `AwaitingConfig`
- `LoadingScene`
- `Ready`
- `Acclimatization`
- `Adaptive`
- `Paused`
- `Stabilization`
- `Completed`
- `Aborted`

Add configurable monotonic timing and a local simulator capable of valid,
stale, noisy, poor-quality, and action-responsive physiology.

**Gate:** A simulated session can pause, resume, stabilize, abort, and run
decision intervals without networking.

## 6. Implement physiology validation and buffering

Implement:

- `StressPayload` based only on the approved schema
- Payload validator with structured rejection reasons
- Freshness and session-ID validation
- Probability and numeric validation
- `PhysiologyStateBuffer`
- Baseline calculation
- HR, RMSSD, and stress trends
- Prevention of physiological-window reuse

> **Later decision:** Embedded session-ID validation is no longer required for
> Component B physiology JSON. Session association is owned by the active
> transport.

**Gate:** Invalid or reused data can never request a learning decision or
produce a model update.

## 7. Implement preference initialization and policy observations

Implement:

- Preference-to-environment blending
- Sensitivity restrictions
- `PolicyObservation`
- `IFeatureVectorBuilder`
- Immutable `FeatureVector`
- Stable feature names, indexes, count, and schema version

The initial feature schema should cover the blueprint categories:

- Physiology
- Preferences
- Session context
- Current environment
- Action history
- Time

Exact inclusion must be frozen before collecting study data.

**Gate:** The same observation always produces the same bounded feature vector
and logs its schema version.

## 8. Implement delayed reward attribution

Implement:

- Pending action-response transition
- Pre-action window reservation
- Transition and settling exclusion
- Post-action window selection
- `RewardCalculator`
- Full reward breakdown
- Invalid-reward reason codes

Invalid data should produce **no update**, not an artificial negative reward.

**Gate:** Pause, emergency, transition overlap, stale data, reused windows, and
network gaps invalidate attribution deterministically.

## 9. Implement baseline policies first

Implement the shared `IEnvironmentPolicy`, then:

- `StaticPersonalizedPolicy`: always selects `NoChange`.
- `RuleBasedAdaptivePolicy`: conservative frozen rules.
- Optional `ManualResearcherPolicy`: development only and still
  safety-validated.

Run both baseline policies through the same session schedule, safety,
environment transition, reward, and telemetry paths intended for LinUCB.

**Gate:** Full simulated sessions work without any policy-specific shortcuts.

## 10. Implement disjoint LinUCB

Implement `IContextualBanditModel` with one model per action:

\[
A_a = \lambda I
\]

\[
b_a = 0
\]

\[
\theta_a = A_a^{-1}b_a
\]

Score:

\[
x^T\theta_a + \alpha\sqrt{x^T A_a^{-1}x}
\]

Update only the executed action:

\[
A_a \leftarrow A_a + xx^T
\]

\[
b_a \leftarrow b_a + xr
\]

Technical requirements:

- `double` matrix calculations.
- Ridge regularization.
- Dimension validation.
- Numerically stable solving rather than unrestricted matrix inversion where
  practical.
- Deterministic tie-breaking.
- `NoChange` always available.
- Matrix work only at decision intervals.
- Candidate scores and uncertainty logged.

**Gate:** Known numerical examples pass and only a valid reward updates the
selected arm.

## 11. Integrate `ContextualBanditPolicy`

The policy should:

- Receive a complete `PolicyObservation`.
- Build one versioned feature vector.
- Score only permitted candidates.
- Select conservatively.
- Return all scores and selection metadata.
- Never manipulate the environment or Unity objects.
- Update only after a valid delayed `ActionOutcome`.

The `PolicyController` then performs final safety validation and environment
application.

**Gate:** Scripted scenarios favoring a particular environmental dimension
cause reproducible adaptation without bypassing safety.

## 12. Add persistence, telemetry, and stabilization

Persist:

- Policy and model version
- Feature schema
- Action list
- Per-action matrices/vectors
- Update count
- Participant pseudonym
- Configuration ID
- Hyperparameters and timestamps

Reject incompatible snapshots explicitly. Keep forgetting disabled initially.

Log every decision, candidate score, proposal, executed action, safety
modification, reward component, skipped update, and model snapshot version.

Implement best-recent-state selection for stabilization with a safe
preference-state fallback.

**Gate:** A session can be reconstructed from JSONL telemetry.

## 13. Validate in increasing scope

### EditMode tests

- Feature ordering and normalization
- Candidate construction
- LinUCB score/update
- Singular and ill-conditioned cases
- Deterministic ties
- Snapshot serialization and mismatch rejection
- Reward attribution
- Invalid-update prevention
- Session transitions

### PlayMode tests

- Complete action-response cycle
- Pause during a pending reward
- Emergency during transition
- Network/stale-data freeze
- Bootstrap policy selection
- Scene adapter integration

### Quest 2 validation

- Sustained full session
- No per-action frame-time spikes
- Network loss freezes adaptation
- Emergency stop remains local
- Persistence survives restart
- No unsafe visual transition

## 14. Freeze the pilot configuration

After simulator, Editor, and Quest validation:

- Calibrate and freeze reward weights.
- Freeze the feature schema.
- Freeze the rule-based baseline.
- Freeze action, safety, and timing parameters.
- Record model, configuration, and package versions.
- Create an ADR for the finalized algorithm, feature schema, reward, and study
  configuration.

Only after the MVP study pipeline is stable should Bayesian Thompson Sampling
or a learned reward model be considered. MAML and PEARL remain out of scope
until adequate multi-user, multi-session data exists.
