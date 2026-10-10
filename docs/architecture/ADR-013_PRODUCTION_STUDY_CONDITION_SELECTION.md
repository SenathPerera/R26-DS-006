# ADR-013: Production study-condition selection

## Status

Accepted for the production session path. This decision selects policy type;
it does not approve unresolved policy parameters.

## Context

The Temple Pond scene previously serialized `ContextualBandit` directly on
`ApplicationBootstrap` and initialized it before a paired session
configuration arrived. That prevented the mobile-owned session configuration
from selecting the assigned study condition and made the assignment difficult
to reconstruct reliably.

The fixed study conditions are:

- `StaticPersonalized`
- `RuleBasedAdaptive`
- `ContextualBandit`

ADR-016 subsequently freezes and assigns the production rule-based profile.

## Decision

The mobile build configuration supplies one explicit `STUDY_CONDITION` value
when preparing a session. The relay validates it, stores it with the prepared
session, and forwards it to Quest in `session_configuration.studyCondition`.
The relay contract was versioned as `mindsync-session-v2` for this required
field. ADR-014 subsequently supersedes the active envelope with
`mindsync-session-v3` while preserving the same study-condition requirement.

Quest maps the exact wire value to `StudyPolicyMode`, rejects unsupported
values, selects the policy before `ApplicationBootstrap` initializes, and
makes the selection immutable for the lifetime of that application/session.
The production scene no longer auto-initializes a serialized policy default.

Session-configuration telemetry records `study_condition`, `policy_id`, and
`policy_version` so the executed condition can be reconstructed independently
of mobile state.

## Safety and research constraints

- Missing, unsupported, or conflicting conditions fail closed.
- No condition bypasses the shared safety validator, transition controller,
  timing, or telemetry pipeline.
- `RuleBasedAdaptive` uses the approved `adaptive-vr-rule-based-pilot-v1`
  profile assigned under ADR-016. No threshold is inferred at runtime.
- Condition assignment is explicit; the app does not randomly assign a
  condition or implement an unapproved counterbalancing schedule.

## Operational consequence

Set one accepted `STUDY_CONDITION` in the mobile `.env` or launching process
before building/starting the mobile app. Preparing a session fails with
`study-condition-not-configured` when it is absent.

Changing the selected condition requires preparing a new session and, once
Quest has initialized, restarting the Quest application before pairing that
new session.
