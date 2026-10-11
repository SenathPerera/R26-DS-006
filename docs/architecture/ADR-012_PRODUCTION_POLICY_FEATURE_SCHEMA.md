# ADR-012: Production policy feature schema

## Status

Accepted for the Temple Pond MVP on 2026-10-10.

## Context

The contextual-bandit implementation previously identified its 24-element
feature vector as `adaptive-vr-policy-features/0.1-draft`. The vector was
deterministic and tested, but the draft identity prevented the policy model,
snapshots, and study telemetry from referring to a frozen production schema.

The broader blueprint lists possible future inputs such as baseline-normalized
HR and RMSSD, physiology trends, session context, action history, and time.
Those inputs do not yet have an approved end-to-end runtime source and bounded
normalization contract. Adding constants or missing-value substitutes would
fabricate research inputs.

## Decision

Freeze the currently implemented vector as
`adaptive-vr-policy-features/1.0`, with exactly 24 ordered features:

| Index | Feature | Range | Definition |
|---:|---|---:|---|
| 0 | `bias` | `1` | Constant intercept |
| 1 | `continuous_stress_score_01` | `[0,1]` | Continuous stress score divided by 3 |
| 2 | `stress_level_0_probability` | `[0,1]` | Relaxed-class probability |
| 3 | `stress_level_1_probability` | `[0,1]` | Mild-class probability |
| 4 | `stress_level_2_probability` | `[0,1]` | Moderate-class probability |
| 5 | `stress_level_3_probability` | `[0,1]` | High-class probability |
| 6 | `signal_quality` | `[0,1]` | Validated signal-quality score |
| 7 | `preferred_illumination` | `[0,1]` | Preferred normalized illumination |
| 8 | `preferred_warmth` | `[0,1]` | Preferred normalized warmth |
| 9 | `preferred_atmospheric_softness` | `[0,1]` | Preferred normalized atmospheric softness |
| 10 | `preferred_color_richness` | `[0,1]` | Preferred normalized color richness |
| 11 | `preferred_ambient_motion` | `[0,1]` | Preferred normalized ambient motion |
| 12 | `current_illumination` | `[0,1]` | Current normalized illumination |
| 13 | `current_warmth` | `[0,1]` | Current normalized warmth |
| 14 | `current_atmospheric_softness` | `[0,1]` | Current normalized atmospheric softness |
| 15 | `current_color_richness` | `[0,1]` | Current normalized color richness |
| 16 | `current_ambient_motion` | `[0,1]` | Current normalized ambient motion |
| 17 | `illumination_delta_from_preference` | `[-1,1]` | Current minus preferred illumination |
| 18 | `warmth_delta_from_preference` | `[-1,1]` | Current minus preferred warmth |
| 19 | `atmospheric_softness_delta_from_preference` | `[-1,1]` | Current minus preferred atmospheric softness |
| 20 | `color_richness_delta_from_preference` | `[-1,1]` | Current minus preferred color richness |
| 21 | `ambient_motion_delta_from_preference` | `[-1,1]` | Current minus preferred ambient motion |
| 22 | `distance_from_preference_01` | `[0,1]` | Five-dimensional Euclidean distance from preference, divided by `sqrt(5)` |
| 23 | `distance_from_safe_default_01` | `[0,1]` | Five-dimensional Euclidean distance from the scene-safe default, divided by `sqrt(5)` |

Feature names, indexes, meanings, normalization, count, and ordering are part
of the schema. Any change to them requires a new feature-schema version and
must not reinterpret an existing model snapshot.

## Deliberate exclusions

- HR, RMSSD, and SDNN are excluded until baseline normalization and missing
  measurement encoding are approved.
- Physiology trends are excluded until bounded slope normalization is approved.
- Mood, fatigue, sleep, subjective stress, desired intensity, time of day,
  session history, and action history are excluded until their collection and
  normalization contracts are available to the Quest policy boundary.
- Scene one-hot features are excluded because Japanese Temple Pond Garden is
  the sole MVP study scene.
- Action identity is not duplicated in the feature vector because disjoint
  LinUCB maintains a separate parameter set for each action.

These exclusions prevent missing values from being silently replaced with
invented measurements. A later approved schema may add them under a new
version.

## Compatibility

Snapshots created with `adaptive-vr-policy-features/0.1-draft` are
incompatible with version `1.0`. Existing exact-match restore validation must
reject them; no automatic migration is permitted because the draft schema was
not approved study data.

The LinUCB profile continues to obtain the version and feature count directly
from `PolicyFeatureVectorBuilder`, preventing configuration drift.

## Consequences

- Decisions and model updates identify the production schema in telemetry.
- New participant models begin with the configured uninformative prior.
- Feature additions require explicit research review, code and test changes,
  snapshot incompatibility handling, and a new ADR or amendment.

