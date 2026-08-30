# ADR-010: Provisional Temple Pond visual configuration

## Status

Accepted as a provisional pilot configuration. This approval permits the
calibrated Japanese Temple Pond Garden profiles to initialize at runtime for
the time-constrained visual MVP. It does not claim that the values are
scientifically optimal or participant-validated.

## Context

The Japanese Temple Pond Garden is the only visual study scene in the MVP
under ADR-004. Its normalized scene profile and raw Unity mapping profile were
calibrated manually in the Unity Editor for visible but gradual changes on the
current scene and water material. Both profiles remained behind their runtime
research-approval gates.

The policy operates only on normalized environment state. The scene adapter is
solely responsible for mapping that state to the raw values recorded here.

## Normalized scene profile

| Setting | Approved value |
|---|---:|
| Default illumination | 0.319 |
| Default warmth | 0.50 |
| Default atmospheric softness | 0.00 |
| Default color richness | 0.50 |
| Default ambient motion | 0.75 |
| Allowed range for every dimension | [0, 1] |
| Illumination action step | 0.10 |
| Warmth action step | 0.25 |
| Atmospheric-softness action step | 0.30 |
| Color-richness action step | 0.20 |
| Ambient-motion action step | 0.20 |
| Transition duration | 4.12 seconds |
| Profile-level minimum seconds between actions | 0 seconds |

The zero profile-level interval does not allow rapid policy decisions. The
approved session timing and production coordinator still enforce the
75-second decision schedule, phase restrictions, transition state, fresh-data
requirements, and session-level action limits.

## Raw Temple mapping profile

| Normalized dimension | Raw Unity mapping |
|---|---|
| Illumination | Directional-light intensity 0.6 to 9.0 |
| Color warmth | Cool `#DDECFF`, neutral `#FFE9DB`, warm `#FFB67B` directional-light colors |
| Atmospheric softness | Exponential-squared fog density 0 to 0.015; clear and soft fog color `#A2BEC5` |
| Color richness | Global URP Color Adjustments saturation -20 to +20 |
| Ambient motion | Water material `_RippleMotion` 0.20 to 0.45 |

The mapping profile is versioned as `temple-pond-mapping-pilot-v3`. Version 3
preserves the third calibrated serialized revision rather than resetting its
history when the profile moves from development to pilot status.

## Decision

Enable the runtime research-approval gates on both Temple Pond profiles without
changing any calibrated numerical or color value. Keep their existing asset
identities and scene references intact.

All proposed policy actions must continue through:

`Policy -> Safety Validator -> Environment Parameter Manager -> Temple Pond Adapter -> Unity Objects`

## Audio-agent boundary

These profiles map only the five normalized visual dimensions. The
teammate-owned adaptive-audio agent and its configuration remain separate.
This decision does not authorize visual code to change audio or audio code to
bypass the visual safety pipeline.

## Limitations

- Values were calibrated visually in the Unity Editor, not through a completed
  participant pilot.
- Quest 2 comfort, sustained performance, and visual legibility still require
  on-device validation.
- Lighting, fog, saturation, and transparent water can appear different on the
  headset from the desktop Game view.
- The current scene and material bindings must be verified after Unity imports
  the approved assets.

## Validation plan

- Reopen both profiles in Unity and confirm the approval gates and exact values.
- Run the complete EditMode and PlayMode suites after production composition
  wiring is complete.
- Exercise each visual action in the serialized Temple scene.
- Validate the full session, safety interruptions, comfort, and performance on
  a physical Quest 2.
