# ADR-004: Temple Pond single-scene study scope

## Status

Accepted.

## Context

The original adaptive VR design anticipated two meditation environments:
Forest Lake and Japanese Temple Pond Garden. Forest Lake has not been built,
and the remaining project schedule does not allow it to be implemented and
validated to the same standard as Temple Pond.

Including an incomplete second scene would expand calibration, integration,
Quest 2 performance, comfort, and research-validation work without improving
the readiness of the primary study pipeline.

## Decision

Japanese Temple Pond Garden is the only supported scene for the MVP study and
the associated pilot configuration. Forest Lake is removed from the planned
study and from the completion criteria.

The policy continues to operate exclusively on the five normalized environment
dimensions. Raw Unity values remain owned by the Temple Pond mapping profile
and scene adapter. The policy must not gain Temple-specific raw properties or
shortcuts merely because only one scene is supported.

## Consequences

- Step 13 scene, Android, and Quest 2 validation applies only to Temple Pond.
- Step 14 freezes one Temple Pond pilot configuration.
- The study must not claim cross-scene adaptation or scene generalization.
- No Forest Lake scene, mapping profile, adapter, or calibration is required
  for MVP completion.
- A future additional scene can still implement the existing normalized scene
  adapter contract without changing the learning policy.

## Validation

- Confirm Temple Pond is the only enabled study scene in Build Settings.
- Run the full production session in Temple Pond.
- Record the Temple Pond scene and configuration identifiers in telemetry and
  validation evidence.
- Treat any future scene addition as separately calibrated and validated work.
