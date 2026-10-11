# ADR-016: Temple Pond pilot research-configuration freeze

## Status

Superseded by ADR-017 for the active Temple Pond engineering-pilot bundle.
This ADR remains the record of `temple-pond-pilot-freeze-v1`, accepted on
2026-10-10.

This is a **pilot protocol freeze**, not a claim that the values are
scientifically optimal or participant-validated. Any change to a frozen value
requires a new configuration ID or version, an ADR amendment, and an update to
the configuration-freeze test.

## Context

The adaptive VR runtime already required explicitly approved ScriptableObject
profiles, but several ADRs still described their values as provisional. There
was also no single versioned list tying those profiles together. The production
scene had no rule-based profile, and it initialized directly at the full
participant preference. Because the rule-based policy only moves toward that
preference, the rule-based condition could otherwise behave identically to the
static condition.

The team approved a shared `65%` participant-preference and `35%` scene-safe
default blend, plus a conservative rule-based activation profile, for the
engineering pilot.

## Decision

Freeze the configuration bundle as:

`temple-pond-pilot-freeze-v1`

The three study conditions continue to share the same scene, initial-state
pipeline, timing, normalized action space, safety validator, transitions,
reward pipeline, stabilization selector, and telemetry system.

### Preference initialization and rule baseline

| Setting | Frozen value |
|---|---:|
| Preference configuration | `adaptive-vr-preference-pilot-v1` / `1` |
| Participant preference weight | `0.65` |
| Scene-safe default weight | `0.35` |
| Rule configuration | `adaptive-vr-rule-based-pilot-v1` / `1` |
| Rule activation | worsening trend **or** elevated stress |
| Elevated continuous-stress threshold | `2.0` |
| Minimum worsening trend | `0.5` stress-score points/minute |
| Minimum preference delta | `0.05` |

The static condition holds the blended initial state. The rule-based condition
moves one dimension at a time from that same initial state toward the full
participant preference when its activation rule is met. The contextual bandit
starts from the same blended state and retains the full preference in its
policy observation.

Stabilization scores eligible outcomes against the full participant preference
but falls back to the shared blended initial state when no eligible outcome
exists.

### Frozen profile identities

| Concern | Configuration identity |
|---|---|
| Temple normalized scene profile | `temple-pond` asset revision frozen by this ADR |
| Temple raw mapping | `temple-pond-mapping-pilot-v3` / `3` |
| Session timing | `temple-pond-session-pilot-v1` / `1` |
| Physiology validation | `component-b-physiology-pilot-v1` / `1` |
| Production coordinator | `adaptive-vr-coordinator-pilot-v1` / `1` |
| Preference initialization | `adaptive-vr-preference-pilot-v1` / `1` |
| Rule-based policy | `adaptive-vr-rule-based-pilot-v1` / `1` |
| LinUCB policy | `adaptive-vr-linucb-pilot-v1` / `1` |
| Reward pipeline | `adaptive-vr-reward-pilot-v2` / `2` |
| Stabilization selection | `adaptive-vr-stabilization-pilot-v1` / `1` |
| Telemetry logging | `adaptive-vr-telemetry-pilot-v2` / `2` |
| Telemetry event schema | `adaptive-vr-telemetry` / `1.0-pilot` |
| Quest reconnect backoff | `component-b-quest-reconnect-pilot-v1` / `1` |
| Policy feature schema | `adaptive-vr-policy-features/1.0` |
| Mobile-to-Quest session schema | `mindsync-session-v3` |

The exact `v1` values were asserted by
`TemplePondPilotConfigurationFreezeTests` when this bundle was accepted.
ADR-017 records the active telemetry-only successor. ADR-001 through ADR-010
and ADR-015 record the rationale and limitations for each profile.

### Model lifetime

The frozen pilot does not automatically load or save LinUCB state between
sessions. Each Quest application/session initialization starts a fresh model.
The snapshot codec and local store remain available infrastructure, but wiring
cross-session learning into the production coordinator requires a separately
approved persistence and forgetting decision.

### Telemetry

`session.config_received` records the active study condition and policy,
configuration IDs and versions for timing, physiology, preference, reward,
stabilization, coordinator, and telemetry, the event-schema identity, the full
participant preference, and the blended initial environment. This makes each
pilot session reconstructable without relying on the mobile build state.

## Not part of the research freeze

Deployment-specific endpoints, local IP addresses, ports, pairing codes, and
mobile `.env` values are operational configuration. They must not be treated as
research parameters or committed as participant-study constants.

## Consequences

- Production can initialize all three study conditions without inventing a
  rule configuration at runtime.
- The rule-based condition is behaviorally distinct from the static condition
  when physiology crosses the frozen activation thresholds.
- Accidental edits to a frozen asset fail the EditMode freeze test.
- Findings from Quest 2 validation or pilot sessions must produce a new bundle
  version instead of mutating `temple-pond-pilot-freeze-v1` in place.
- This freeze does not replace the required on-device comfort, performance,
  full-session, and telemetry validation.

## Validation

- Run the complete Unity EditMode and PlayMode suites.
- Run one full session for each study condition and verify policy identity,
  initialization, decisions, rewards, and stabilization in JSONL telemetry.
- Validate all visual extrema and transitions on a physical Quest 2.
- Confirm the rule-based condition produces `NoChange` below its thresholds and
  preference-directed actions above them.
