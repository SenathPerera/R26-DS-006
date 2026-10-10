# ADR-017: Complete pilot telemetry coverage

## Status

Accepted for the Temple Pond engineering pilot on 2026-10-11.

This decision creates the active configuration bundle:

`temple-pond-pilot-freeze-v2`

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
| Pilot bundle | `temple-pond-pilot-freeze-v2` |
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
