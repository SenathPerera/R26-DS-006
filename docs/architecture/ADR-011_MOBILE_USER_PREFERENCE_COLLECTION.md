# ADR-011: Mobile user-preference and session-context collection

## Status

Accepted

## Context

The Japanese Temple Pond Garden needs a persistent visual preference profile for
each participant and a separate description of the participant's current state
before each VR session. The previous mobile flow sent one hardcoded environment
preference to every Quest session and did not distinguish persistent preferences
from temporary adjustments.

## Decision

- Collect the five normalized environment preferences, particle preference,
  light sensitivity, and motion sensitivity during account onboarding.
- Persist those long-term values in the participant's Supabase onboarding row.
- Before every new VR session, collect subjective stress, mood valence, fatigue,
  sleep quality, headache or eye strain, and whether the participant wants their
  usual garden or temporary adjustments.
- Store the session-only context and the effective five-dimensional preference
  on the meditation-session row. Temporary values never update onboarding.
- Derive time of day, session sequence number, and days since the previous
  session on the mobile device when the context is submitted.
- Continue sending only the effective normalized five-dimensional environment
  preference through the existing relay contract. Unity retains ownership of
  safety validation and raw scene mapping.
- Keep the Japanese Temple Pond Garden as the only selectable scene.

## Alternatives considered

- Keep using a hardcoded Temple Pond preference. This would not personalize
  initialization.
- Store temporary values in onboarding. This would silently change the user's
  long-term profile.
- Expose raw Unity settings in the app. This would couple the mobile UI to one
  scene mapping and bypass the normalized environment model.

## Consequences

- A Supabase migration is required before the updated mobile app can persist the
  new fields.
- Existing onboarding rows can retain null values and are read with the current
  safe Temple Pond defaults for compatibility.
- Particle and sensitivity values are collected and persisted, but the current
  relay message still transports only the five normalized environment values.
  Applying the extra safety constraints on Quest requires a separately versioned
  relay/Unity contract change.

## Validation plan

- Unit-test usual versus temporary preference resolution, normalization, and
  automatic context fields.
- Unit-test Supabase row mapping for the persistent and session-only values.
- Verify onboarding persistence with a newly created account.
- Verify that two sessions can use different temporary values while the
  onboarding row remains unchanged.
- Verify that the effective preference sent to the relay matches the selected
  usual or temporary values.
