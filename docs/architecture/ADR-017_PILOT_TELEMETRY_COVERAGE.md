# ADR-017: Complete pilot telemetry coverage

## Status

Accepted for the Temple Pond engineering pilot on 2026-10-11. Amended on
2026-10-18 for the Quest visual-strength calibration described below. The
telemetry decision remains in effect.

The original telemetry decision created the configuration bundle:

`temple-pond-pilot-freeze-v2`

This was the active bundle until the visual-strength amendment below created
`temple-pond-pilot-freeze-v3`.

It inherits every research setting from `temple-pond-pilot-freeze-v1` except
the telemetry profile and event-schema versions identified below.

## Context

Schema `1.0-pilot` recorded the adaptive decision, safety, reward, and session
paths, but several declared events were not emitted by the production
coordinator. Physiology records also omitted measurements needed to reproduce
reward inputs, rejected non-finite measurements could not be represented
safely as JSON numbers, and relay delivery outcomes were not written to the
durable local log.

A pilot session must remain reconstructable from its local JSONL file even
when the relay is unavailable. Closing these gaps changes the event payload
contract, so the existing frozen profile must not be edited in place without a
new identity and schema version.

## Decision

Use the following active telemetry identities:

| Concern | Active identity |
|---|---|
| Pilot bundle at the telemetry decision | `temple-pond-pilot-freeze-v2` |
| Telemetry configuration | `adaptive-vr-telemetry-pilot-v3` / `3` |
| Event schema | `adaptive-vr-telemetry` / `1.1-pilot` |
| Ordinary-event flush batch | 8 events |

The set of event names in `TelemetryEventTypes` remains stable. Schema
`1.1-pilot` completes their production coverage and adds the following
payload guarantees:

- application version, Unity version, build GUID, platform, and non-unique
  device/runtime information are recorded at session initialization;
- the already-loaded Unity scene and its binding-validation outcome are
  recorded explicitly. The scene events state that loading was observed
  retrospectively because session telemetry starts after the scene exists;
- accepted and rejected physiology records contain timestamps, window
  duration, heart rate, optional RMSSD/SDNN, signal quality, stress decision,
  probabilities, and continuous stress score;
- a missing or non-finite physiological value is represented as JSON `null`
  rather than fabricated, clamped, or serialized as an invalid JSON number;
- participant-preference and stabilization transitions use the same started,
  completed, and cancelled event family as adaptive transitions and include a
  transition reason;
- terminal session events include the termination reason, policy identity,
  decision/outcome/update counts, accepted physiology count, total environment
  variation, and final normalized environment;
- relay telemetry-batch acknowledgements and publish failures are written to
  the durable local log with the affected sequence range; and
- performance samples are emitted at event-driven checkpoints: session
  initialization, decision opportunities, non-adaptive transition completion
  or cancellation, and terminal phases.

No fixed performance-sampling interval is introduced. Selecting such an
interval is a separate performance/research decision; Quest profiling remains
the authority for continuous frame-time and thermal validation.

## Relay recursion prevention

`local_log.upload_succeeded` and `local_log.upload_failed` are durable local
events, but they are not added to the outbound relay queue. Otherwise each
acknowledgement would create another event requiring another acknowledgement.
All other events continue to be written locally before becoming eligible for
relay transfer.

## Privacy and failure behavior

- Device telemetry excludes advertising IDs, Android IDs, serial numbers, IP
  addresses, and other unique device identifiers.
- Participant identity remains pseudonymous.
- Local persistence remains independent of relay connectivity.
- Critical lifecycle, terminal, and transfer-result events force a flush.
- Invalid physiological input is logged with its validation reason without
  being used by the adaptation or reward pipelines.

## Consequences

- `temple-pond-pilot-freeze-v1`, telemetry profile `v2`, and schema
  `1.0-pilot` remain immutable historical identities.
- Consumers that validate schema versions must accept `1.1-pilot` before
  ingesting new pilot sessions.
- The configuration-freeze test now protects telemetry profile `v3` and
  schema `1.1-pilot` as part of the active bundle.
- A complete session can be audited from initialization through terminal state
  without relying on relay availability.

## Validation

- Build the runtime, EditMode-test, and PlayMode-test assemblies.
- Run the complete Unity EditMode and PlayMode suites.
- Verify startup, full physiology, null-safe rejection, and preference
  transition events in the coordinator PlayMode test.
- Verify transfer-result events remain durable locally without entering the
  outbound relay queue.
- Run one full session per study condition and inspect the JSONL ordering,
  sequence continuity, terminal summary, and absence of direct identifiers.
- Profile a full session on a physical Quest 2; Editor timing is not evidence
  of headset performance.

## 2026-10-18 visual-strength amendment

This creates `temple-pond-pilot-freeze-v3` as the active **engineering** bundle.
It supersedes the visual values in ADR-010 while preserving this ADR's
telemetry settings and all other frozen study settings.

TODO(RESEARCH_DECISION): Confirm perceptibility, comfort, and Quest 2 frame
timing at all five dimensions' extrema and during individual transitions before
using these values for participant data collection. These are calibration
candidates, not participant-validated safety thresholds.

Quest telemetry from the 2026-10-17 and 2026-10-18 engineering sessions showed
14 executed `NoChange` actions and only three executed visual changes across 21
session files. In the longest recent adaptive test, ambient motion moved from
0.9125 to 1.0, while illumination moved from 0.319 to 0.419 and then back to
0.319. Most dimensions therefore had no executed action. All five normalized
ranges already allow `[0, 1]`; weak visual effects cannot be attributed to
those limits.

The user requested stronger *executed* visual actions while retaining the
contextual-bandit selection behavior. This amendment changes only the Temple
Pond scene profile and its scene-specific mapping. `NoChange`, the action
space, safety validator, total-variation budget, decision schedule, and
physiological/reward logic are unchanged.

Larger normalized steps consume more of the unchanged total-variation budget,
so later non-`NoChange` candidates can become ineligible sooner. That effect
must be counted during Quest validation; this amendment does not promise more
frequent visual changes.

| Setting | Previous | Engineering calibration |
|---|---:|---:|
| Illumination normalized action step | 0.10 | 0.15 |
| Warmth normalized action step | 0.25 | 0.35 |
| Atmospheric-softness normalized action step | 0.30 | 0.40 |
| Color-richness normalized action step | 0.20 | 0.30 |
| Ambient-motion normalized action step | 0.20 | 0.30 |
| Transition duration | 4.12 s | 6 s |
| Saturation range | -20 to +20 | -30 to +30 |
| Water `_RippleMotion` range | 0.20 to 0.45 | 0.10 to 0.60 |

The calibrated profiles are separate Unity assets:
`TemplePondSceneParameterProfile_VisualV3.asset` and
`TemplePondEnvironmentMappingProfile_v4.asset`. The scene references these new
assets; the previous profiles and their `.meta` identities remain intact for
the historical bundle. The raw Temple mapping identity advances from
`temple-pond-mapping-pilot-v3` / `3` to
`temple-pond-mapping-pilot-v4` / `4`. The normalized scene profile retains its
`temple-pond` scene identity and is tracked as part of the new bundle. Its safe
defaults and full normalized bounds remain unchanged.

Directional-light intensity stays at 0.6 to 9.0, and fog density stays at 0
to 0.015. Their action steps increase without raising already broad endpoints.
Warmth color anchors and sky/bird speed extrema remain unchanged. The longer
transition limits the rate of the larger normalized steps.

The configuration-freeze EditMode test checks the revised serialized values.
On Quest 2, exercise each positive and negative action from a state with room
to move in both directions, then check the full transition and both extrema
for comfort, visual legibility, and frame timing. The bandit may select
`NoChange`, so a normal adaptive session cannot validate all five visual
dimensions. Action frequency is an independent policy behavior outside this
amendment.

The frozen research-evaluation loader still uses the historical scene profile.
Its existing simulation results describe the previous visual bundle; rerun and
version that evaluation before making comparative claims about this calibration.
