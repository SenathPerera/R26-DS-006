# ADR-014: Mobile-to-Quest session contract

## Status

Accepted for the Temple Pond MVP on 2026-10-10.

## Context

The production relay contract already carried session identity, the Temple Pond
scene, the assigned study condition, and the resolved five-dimensional visual
preference. Mobile also collected per-session context, but that context stopped
at mobile storage. The earlier onboarding design collected light and motion
sensitivity even though the scoped Temple Pond scene has no locomotion or
policy-controlled camera motion and its only ambient motion is gradual water
rippling.

ADR-012 deliberately excludes session-context features from production policy
schema `adaptive-vr-policy-features/1.0`. Adding them to that frozen vector now
would reinterpret existing models and require separate research approval.

## Decision

Version the relay envelope as `mindsync-session-v3`, defined by
`contracts/session/mindsync-session-v3.schema.json`. A prepared session and its
Quest `session_configuration` message require:

- `sessionId`
- `participantPseudonym`
- `sceneId`
- `studyCondition`
- the five normalized values under `preferredEnvironment`
- `sessionContext` using `mindsync-session-context-v1`

The Quest-facing session context contains only:

- `collectedAt`
- `subjectiveStress` in `[0, 10]`
- `moodValence` in `[-1, 1]`
- `fatigue` and `sleepQuality` in `[0, 1]`
- `headacheOrEyeStrainToday`
- automatic `timeOfDayMinutes`, `sessionSequenceNumber`, and nullable
  `daysSincePreviousSession`

Mobile-only preference mechanics (`preferenceMode` and temporary slider values)
are not duplicated on the wire. Mobile resolves them into the single
`preferredEnvironment` sent to Quest.

ADR-016 freezes how Quest uses that full preference: all conditions initialize
from `65%` participant preference and `35%` Temple Pond safe default. The full
preference remains available to policy observations and stabilization scoring.

Quest validates and records session context in session-configuration telemetry.
It does not add those values to policy observations, safety decisions, rewards,
or feature schema `1.0`. A future policy use requires a new approved feature
schema and model compatibility decision.

Light- and motion-sensitivity questions and persistence columns are removed.
No substitute sensitivity values are fabricated. Existing scene limits,
gradual transitions, cooldowns, total-variation limits, and emergency controls
remain in force. The generic Unity ability to apply additional session limits
is retained for future approved safety requirements.

## Compatibility

Version 3 is intentionally incompatible with version 2. Mobile, relay, and
Quest must be deployed with the same relay schema version. Missing, malformed,
out-of-range, or unsupported session context fails before Quest session
initialization.

## Consequences

- Apply the Supabase migration that drops `light_sensitivity` and
  `motion_sensitivity` from `onboarding_profiles`.
- Existing stored session-context JSON remains valid on mobile; only the
  narrowed projection crosses the relay boundary.
- Research analysis can associate pre-session state with Quest telemetry
  without changing the active learning inputs.
