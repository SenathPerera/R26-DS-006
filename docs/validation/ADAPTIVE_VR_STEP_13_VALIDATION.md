# Adaptive VR Step 13 Validation

**Scope:** Original 14-step plan, Step 13 — validate in increasing scope  
**Target:** Unity 6 LTS, standalone Android, Meta Quest 2  
**Status:** In progress; Editor and device evidence must be recorded separately

## Evidence rules

- A passing EditMode suite proves deterministic domain and integration logic,
  not scene behavior or Quest behavior.
- A passing PlayMode suite proves the isolated runtime pipeline, not the
  production meditation scenes.
- An Android build proves player compilation, not headset comfort or sustained
  performance.
- Quest criteria remain `Not run` until observed on a physical Quest 2.

## Baseline

| Item | Recorded value |
|---|---|
| Baseline commit | `60ce7cb` |
| Unity version | `6000.0.82f1` |
| Render pipeline | URP `17.0.4` |
| XR provider | OpenXR `1.16.1` |
| Target | Android / ARM64 / IL2CPP |
| Enabled build scene | `JapaneseTemplePondGarden.unity` |
| Forest Lake | Not yet implemented |
| Android package ID | Placeholder Unity template ID; must be finalized before a release build |

## EditMode validation

Run the complete EditMode test assembly and retain the Test Runner result or
exported XML.

| Criterion | Status | Required evidence |
|---|---|---|
| Feature ordering and normalization | Passed | Full EditMode suite reported passing on 2026-08-29 |
| Candidate construction | Passed | Full EditMode suite reported passing on 2026-08-29 |
| LinUCB scoring and updates | Passed | Full EditMode suite reported passing on 2026-08-29 |
| Ill-conditioned numerical cases | Passed | Full EditMode suite reported passing on 2026-08-29 |
| Deterministic ties | Passed | Full EditMode suite reported passing on 2026-08-29 |
| Snapshot save/load and mismatch rejection | Passed | Full EditMode suite reported passing on 2026-08-29 |
| Reward attribution and invalid-update prevention | Passed | Full EditMode suite reported passing on 2026-08-29 |
| Session transitions | Passed | Full EditMode suite reported passing on 2026-08-29 |

## PlayMode validation

Open **Window → General → Test Runner → PlayMode**, run
`AdaptiveLearningPipelinePlayModeTests`, and record the result here.

| Test | Status before run | Purpose |
|---|---|---|
| `CompleteActionResponseCycle_UpdatesBanditAfterReward` | Not run | Full decision, reward, and selected-arm update |
| `PauseDuringPendingReward_InvalidatesWithoutUpdate` | Not run | Pause boundary prevents learning |
| `EmergencyDuringTransition_CancelsAndFreezesState` | Not run | Local emergency cancellation preserves the current safe state |
| `NetworkAndStalePhysiology_FreezeNewDecisions` | Not run | Network and freshness failures prevent adaptation |
| `BootstrapPolicySelection_CreatesAllStudyPolicies` | Not run | Runtime factory selects all three study policies |
| `SceneAdapterIntegration_AppliesSmoothTransitionPerFrame` | Not run | Unity component receives gradual normalized states |

These tests use an isolated GameObject adapter. Production Temple/Forest scene
adapter validation remains pending until scene-specific adapters are present.

## Android build validation

Do not overwrite an existing build. Use a new output directory outside
`Assets/`, `Packages/`, and `ProjectSettings/`.

1. Confirm the active target is Android.
2. Confirm IL2CPP and ARM64.
3. Confirm the enabled scenes are intentional.
4. Produce a Development APK in a new build-output directory.
5. Record Unity Console errors/warnings and the artifact path.
6. Do not treat the placeholder package identifier as release-ready.

Current status: **Not run**.

## Quest 2 validation checklist

Run on a physical Quest 2 after installing the development APK. Preserve the
device log and profiler capture where applicable.

| ID | Scenario | Pass criteria | Status |
|---|---|---|---|
| Q2-01 | Sustained full session | Session completes without crash, tracking loss, or unsafe phase transition | Not run |
| Q2-02 | Per-action performance | Profiler shows no repeatable action-correlated frame-time spike; final numeric budget remains `TODO(RESEARCH_DECISION)` | Not run |
| Q2-03 | Network loss | Current safe environment remains rendered and no new decisions execute | Not run |
| Q2-04 | Local emergency stop | Emergency stop works with transport disconnected and cancels pending adaptation | Not run |
| Q2-05 | Persistence restart | Saved participant model restores after application restart; mismatched participant/snapshot is rejected | Not run |
| Q2-06 | Visual comfort | No flash, abrupt exposure/fog change, near-face motion, camera motion, or unsafe transition | Not run |
| Q2-07 | Pause/resume | Pause freezes learning; resume requires fresh valid physiology | Not run |
| Q2-08 | Headset lifecycle | Sleep/focus loss does not fabricate input or resume adaptation unsafely | Not run |

Record for every run:

- Git commit and configuration IDs.
- APK path and version.
- Quest OS/runtime version.
- Scene and session duration.
- Test participant pseudonym only.
- JSONL telemetry path and model snapshot ID.
- Device log path.
- Profiler capture path when performance is assessed.
- Failure description and reproduction steps.

## Current limitations

- Forest Lake is not available.
- Production scene-specific environment adapters and application bootstrap are
  not yet present, so their PlayMode criteria cannot be validated against a
  meditation scene.
- No Android artifact or Quest 2 evidence has been produced in this step yet.
- The final Quest performance threshold is an unresolved research decision.
