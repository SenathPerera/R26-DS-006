# Longitudinal Study A — investigator freeze record v1.0

**Date:** 2026-10-19 (Asia/Colombo)  
**Protocol:** `laminar-vr-longitudinal-simulation-v1.0`  
**Decision:** The investigator requested approval and freezing of the proposed
matrix after the development smoke. The optional practical-margin endpoint was
explicitly omitted. This records investigator approval of the evaluation-only
simulation design; no supervisor or ethics approval is asserted here.

## Frozen matrix

| Item | Value |
|---|---|
| Primary archetypes | A1–A7, stationary responsive |
| Conditions | Carry LinUCB, Reset LinUCB, Static Personalized, Rule-Based Adaptive |
| Replications | 30 synthetic profiles per archetype, seeds 410000–410029 |
| Sessions and probes | 20 sessions per profile; 32 fixed held-out probes at baseline and after each session |
| Primary sessions | 16,800 |
| Challenges | Between-session drift, A1 with 10% physiology loss, and A9 nonresponder; Carry and Reset only; 30 profiles each, seeds 420000–420029 |
| Challenge sessions | 3,600 |
| Total | 20,400 simulated sessions; 1,020 profile-condition trajectories; 685,440 probe rows expected |
| Development-only seeds | 510000 primary and 520000 challenges; excluded from the full-study analysis |
| Primary endpoint | A1–A7 equal-archetype-weighted, paired Carry−Reset mean safe-oracle probe pseudo-regret after session 20 |
| Primary inference | 30 seed-index contrasts; two-sided 100,000-draw sign-flip test, alpha 0.05; 10,000-draw paired bootstrap interval |
| Inferential streams | PCG32 930001/7 sign flips and 940001/8 bootstrap |
| Optional practical-margin endpoint | Omitted |

The production-like decision and safety pipeline, synthetic response generator,
probe generation, evaluation-only model persistence, session seed formulas, and
visual calibration are specified in the [v1.0 protocol](LAMINAR_VR_LONGITUDINAL_SIMULATION_PROTOCOL_v1.0.md)
and [v1.0 configuration](../../analysis/component-c-bandit-evaluation/configs/longitudinal-study-v1.0.json).
The visual lock pins the Temple Pond VisualV3 scene profile, v4 mapping, larger
normalized action steps `(0.15, 0.35, 0.40, 0.30, 0.30)`, six-second transition,
and their asset hashes.

## Evidence and scope

- The investigator reported that all EditMode and PlayMode tests passed.
- The completed v0.2 development smoke run
  `20261019T155004Z-longitudinal-smoke` passed artifact analysis: 68 sessions,
  34 trajectories, 408 probe rows. It used development seeds and a dirty
  working tree; it is validation evidence, not a confirmatory result.
- The full-study seed families have not been run or inspected as of this
  record. The full-study result must be reported regardless of direction.
- This freeze authorizes evaluation-only carry-over in the simulator. It does
  not authorize participant-linked model persistence in the deployed Quest app.

## Required provenance and governance

The full run manifest must record the v1.0 protocol/configuration and analysis
script hashes, Unity version, commit, dirty status, visual asset hashes, seeds,
and completed artifact checksums. Run the v1.0 visual preflight and analyze the
completed full output with the v1.0 analyzer. Archive the exact source revision
used for the run. If the working tree is dirty, preserve a patch alongside the
manifest before interpreting results.

**Supervisor/team sign-off:** not recorded. If the institution requires it,
record the approver, date, and approved v1.0 identifier before describing the
study as formally pre-registered. Any change to the frozen matrix, generator,
probe algorithm, endpoint, analysis rule, or visual calibration needs a new
version before using affected confirmatory seeds.
