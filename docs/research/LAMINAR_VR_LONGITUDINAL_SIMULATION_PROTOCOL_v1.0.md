# Laminar VR — longitudinal simulation protocol (frozen Study A)

**Protocol ID:** `laminar-vr-longitudinal-simulation-v1.0`  
**Status:** Investigator-frozen for the reserved full-study seeds; supervisor/team sign-off has not been recorded  
**Date:** 2026-10-19  
**Scope:** Component C, adaptive *visual* environment, Japanese Temple Pond Garden

## 1. Purpose and evidence boundary

Evaluate whether retaining a safety-constrained contextual-bandit model for the
same synthetic individual across repeated sessions improves its *decisions*
under specified synthetic response models. This is the longitudinal **Study A**
in the proposed two-part evaluation; the short real-user feasibility/pilot test
is **Study B** and needs its own protocol and ethics review.

This is separate from the already completed, frozen
[`component-c-simulation-evaluation-v1.2`](COMPONENT_C_SIMULATION_EVALUATION_PROTOCOL.md),
whose experiment named “Study A” compared policies over **one session per
replication**. Call its result “the prior single-session Study A” in reports so
the two datasets cannot be mistaken for each other. Do not edit its manifests,
configurations, results, or conclusions. In particular, its fresh-within-session
LinUCB did not outperform Random Safe on its predeclared primary contrast.

Synthetic stress, heart rate, RMSSD, preferences, and comfort responses are
engineering constructs. This study cannot establish stress reduction,
personalization, usability, comfort, or safety in people. Study B will supply
initial human feasibility and outcome observations; it cannot retroactively
validate the synthetic response law.

## 2. Research questions and hypotheses

- **RQ-A1:** Does a same-user model carried across sessions choose better safe
  actions on held-out, fixed decision probes than the identical LinUCB model
  reset at each session?
- **RQ-A2:** Does performance on complete simulated sessions improve with
  cumulative *valid* model updates, and how much feedback is needed before an
  advantage appears?
- **RQ-A3:** How does a carried model compare with Static Personalized and the
  frozen Rule-Based Adaptive policy on synthetic outcomes and safety costs?
- **RQ-A4:** Does model carry-over help under preference/response conflict and
  weak response, and what happens when the response changes between sessions
  or valid physiology becomes scarce?

**Primary directional hypothesis H-A1:** in the stationary responsive
archetypes A1–A7, the carried-model LinUCB has lower mean safe-oracle
pseudo-regret on the common held-out probe set **after session 20** than the
session-reset LinUCB after session 20. Define the paired difference as
`carried − reset`; a negative value supports H-A1. This is a hypothesis about
the declared simulator, not people. An equal or worse result is reportable and
must not trigger post-hoc tuning.

**Secondary hypotheses:** H-A2 predicts a decline in carried-model probe
regret between the pre-training checkpoint and after session 20. H-A3 predicts
zero executed safety violations and zero invalid model updates in every run.
Between-session drift, sparse feedback, and other policy rankings are
preplanned descriptive or sensitivity analyses, not extra confirmatory tests.

## 3. System and model lifetime

Use the active Temple Pond **`temple-pond-pilot-freeze-v3` visual calibration**
from the [ADR-017 visual-strength amendment](../architecture/ADR-017_PILOT_TELEMETRY_COVERAGE.md)
and the production five-dimensional normalized environment, 11 actions including
`NoChange`, 24-feature `adaptive-vr-policy-features/1.0` vector, disjoint LinUCB
(`alpha = 0.25`, ridge `= 1.00`), preference initialization, candidate filter,
safety validator, environment manager, transition controller, physiology
validation, reward attribution, and telemetry as the prior protocol. The
existing timeline is acclimatization `[0,120)` seconds, adaptation `[120,1020)`,
and stabilization `[1020,1170]`, with 11 nominal decisions at 75-second
spacing. A simulated session advances by events, not elapsed wall time.

| Visual setting | Study A value | Source asset |
|---|---:|---|
| Normalized action steps: illumination, warmth, softness, richness, motion | `(0.15, 0.35, 0.40, 0.30, 0.30)` | `TemplePondSceneParameterProfile_VisualV3.asset` |
| Transition duration | `6 s` | `TemplePondSceneParameterProfile_VisualV3.asset` |
| Safe default | `(0.319, 0.50, 0, 0.50, 0.75)` | `TemplePondSceneParameterProfile_VisualV3.asset` |
| Normalized limits | `[0,1]` for every dimension | `TemplePondSceneParameterProfile_VisualV3.asset` |
| Saturation mapping | `−30` to `+30` | `TemplePondEnvironmentMappingProfile_v4.asset` |
| Water `_RippleMotion` mapping | `0.10` to `0.60` | `TemplePondEnvironmentMappingProfile_v4.asset` |
| Mapping identity | `temple-pond-mapping-pilot-v4` / `4` | `TemplePondEnvironmentMappingProfile_v4.asset` |

The illumination light-intensity and softness fog-density endpoints remain
`0.6–9.0` and `0–0.015`, respectively. The safety total-variation budget,
repeated-direction limit, reward weights, signal-quality gates, and decision
schedule are unchanged. Larger normalized steps can use the variation budget
faster; the longer transition can change reward-window timing. Count resulting
candidate exclusions, delayed rewards, skipped decisions, and valid updates
as study outcomes. **Do not copy the previous protocol's `4.12 s` transition
or `(0.10, 0.25, 0.30, 0.20, 0.20)` steps.**

The prior `FrozenEvaluationProfileLoader.TryLoad` still loads
`TemplePondSceneParameterProfile.asset` for the historical v1.2 runs, and
`EvaluationRuntimeBundle` still labels that historical pilot as v2. The new
longitudinal runner must load the **VisualV3** asset explicitly, validate the
v4 mapping asset and the scene's references to both, and record the v3 bundle
and both asset hashes in its manifest. It must fail before a run if an old or
mismatched profile is supplied. The machine-readable visual lock and its
preflight validator are
[`longitudinal-visual-v1.0.json`](../../analysis/component-c-bandit-evaluation/configs/longitudinal-visual-v1.0.json)
and
[`validate_longitudinal_visual_profile_v1.py`](../../analysis/component-c-bandit-evaluation/scripts/validate_longitudinal_visual_profile_v1.py);
they do not change historical simulation artifacts.

The simulator computes responses from **normalized environmental states**.
The v3 action steps and transition timing therefore change simulated actions
and attribution. Raw saturation and ripple mappings are recorded and checked
for configuration fidelity, but the physiology generator has no perceptual
model of those Unity properties. A better synthetic outcome cannot establish
that the stronger scene mapping is more effective or comfortable for people.

The **deployed pilot** currently starts a fresh LinUCB model for every session
([ADR-016](../architecture/ADR-016_TEMPLE_POND_PILOT_CONFIGURATION_FREEZE.md)).
`EvaluationSessionRunner` also calls `Reset(NewSession)`. Versioned LinUCB
snapshot capture/restore exists, but production cross-session loading is not
wired. Therefore carried-model LinUCB below is an **evaluation-only
counterfactual configuration**, not a description of current Quest behavior.
Implementing its experimental harness requires a separately versioned
model-lifetime setting and tests; enabling it in the production coordinator
requires a new approved ADR covering participant association, retention,
forgetting, consent, and failure handling. Do not quietly alter the pilot
study condition or compare newly generated data with historical runs as if
the configuration were the same.

The current feature schema does **not** include fatigue, sleep, mood, time of
day, session number, or session history ([ADR-012](../architecture/ADR-012_PRODUCTION_POLICY_FEATURE_SCHEMA.md)).
This protocol evaluates repeated feedback for an individual. It must not claim
explicit session-context personalization unless a new sourced and versioned
feature schema is approved and studied separately.

## 4. Conditions and unit of replication

| Condition | Within-session behavior | Between-session state |
|---|---|---|
| **Carry LinUCB** | Frozen production LinUCB | Save valid final model snapshot under synthetic profile ID; restore it before the next session. No forgetting in the primary experiment. |
| **Reset LinUCB** | Identical LinUCB, reward and safety path | Reset to the same uninformative prior each session. |
| **Static Personalized** | Hold the shared blended initial environment | No learned state. |
| **Rule-Based Adaptive** | Frozen rule profile and shared safety path | No learned state. |

The primary comparison is Carry versus Reset. Static and Rule-Based are
practical, prespecified reference policies. All four conditions receive the
same synthetic profile, preference, session sequence, exogenous random draws,
scene, initial environment, nominal session timing, and safety rules. Their
actions cause different later states; **do not force their physiological
trajectories to remain identical**. The independent sampling unit is one
synthetic individual (archetype × seed) with a complete repeated-session
trajectory. Sessions and decisions within an individual are correlated and
must not be counted as independent participants.

Only the previous session's **model** crosses the session boundary. A new
session receives a fresh session ID, phase machine, baseline, physiology
buffer, reward tracker, environment initial state, safety variation/cooldown
budgets, and telemetry stream. Clear any pending action outcome at termination.
Never restore a stale reward window, prior environmental state, or prior
session's physiology as a new observation. Snapshot restore must validate
synthetic profile identity, policy/configuration/feature/snapshot versions,
arm ordering, hyperparameters, finite values, and update counts. Failure is
logged and fails that profile's carry run closed; it is not silently replaced
with a fresh model in the primary analysis.

## 5. Synthetic individuals and response generator

Use the **previously frozen** response generator and A1–A9 archetypes from
Sections 7–8 of the single-session protocol. This holds the physiology and
reward mechanism fixed while testing model lifetime. For each synthetic
individual, draw one preference vector and one archetype-specific optimum and
weight vector, then retain them across its sessions. Draw fresh session
baseline stress, heart rate, RMSSD, natural recovery, and observation noise
from the existing specified distributions. Starting each session from the
same **distribution**, not the previous session's final stress, prevents
simulator-induced cumulative recovery from masquerading as policy learning.

The primary stratum is stationary responsive A1–A7. Report each archetype
separately; do not hide the weak-response (A7) or preference-conflict (A6)
results in the pooled mean. A9 is a negative control: its environment effect
is zero, so any claimed physiological benefit over the paired reference needs
investigation. The prior protocol's A8 within-session change point is not part
of this matrix or the stationary primary contrast.

**Between-session drift challenge:** on the first window of session 11,
change only the illumination and warmth optima to
`clip(1 − preference, 0.20, 0.80)` and retain that changed optimum through
session 20. This challenge is distinct from A8's existing *within-session*
change at 570 seconds. Use the A1 response law otherwise. Compare sessions
1–10, 11–12, and 13–20 descriptively; a carried model without forgetting may
adapt slowly or perform worse. Do not add forgetting to the primary condition
after seeing these results. A later forgetting variant needs its own versioned
protocol and independent validation seeds.

**Sparse-feedback challenge:** repeat A1 with the prior protocol's R3
independent 10% physiology-emission loss, including acclimatization. Invalid,
missing, stale, or low-quality windows cannot create a decision or a model
update. Sessions lacking baseline remain availability outcomes.

The generator emits physiological payloads; it never supplies reward directly
to LinUCB. Production validation and attribution calculate every learning
reward. The simulator's latent utility is available only to analysis and
oracle code. Do not parameterize synthetic effect sizes from Study B pilot
outcomes without a separate amendment and independent validation split.

## 6. Frozen experiment matrix

These are the investigator-approved Study A simulation settings, frozen before
any full-study seed is run. They do not change production defaults. The
machine-readable configuration is
[`longitudinal-study-v1.0.json`](../../analysis/component-c-bandit-evaluation/configs/longitudinal-study-v1.0.json).
The inspected v0.2 smoke run used separate development seeds and is excluded
from confirmatory inference.

| Design item | Frozen value | Rationale |
|---|---:|---|
| Sessions per synthetic individual | 20 | About 220 nominal decisions; enough to inspect the learning curve while retaining the actual sparse per-session timing. |
| Primary archetypes | A1–A7 | Seven distinct stationary responsive response models. |
| Independent seed/profile replications | 30 per archetype | 210 independent synthetic individuals; report Monte Carlo precision rather than claiming population power. |
| Primary conditions | Carry, Reset, Static, Rule-Based | One causal model-lifetime contrast plus deployed-policy references. |
| Primary simulated sessions | `7 × 30 × 20 × 4 = 16,800` | Fixed matrix; one full trajectory per condition/profile. |
| Drift and sparse-feedback challenge | 30 profiles × 20 sessions × Carry and Reset, each | `2 × 30 × 20 × 2 = 2,400` additional sessions. |
| Negative-control A9 | 30 profiles × 20 sessions × Carry and Reset | 1,200 additional sessions. |
| Probe contexts | 32 fixed, held-out, valid contexts per profile | Common decision test at every checkpoint, never used as feedback. |
| Seed families | `410000–410029` primary; `420000–420029` challenges | Distinct from the prior protocol's seeds. |
| Development smoke seeds | `510000` primary; `520000` challenges | Keep inspected smoke outcomes outside the full-study seed families. |
| Primary endpoint checkpoint | After session 20 | Fixed before inspection. |

The total frozen matrix is 20,400 simulated sessions. Do not reduce, stop early
on favorable results, or expand only unfavorable cells. Any change to these
settings requires a new protocol version before running affected full-study
seeds; report any completed v1.0 runs separately.

## 7. Randomization and leakage control

Use the prior harness's documented PCG32 implementation with independent,
named streams for profile/preference, per-session baseline, physiology noise,
loss/fault schedule, and probe generation. The frozen v1.0 implementation
uses the predeclared `profile_seed` to draw stable preferences, derives a fresh
`session_seed = profile_seed × 100 + session_index` for baseline and emission
streams, and derives held-out probe seeds from `profile_seed × 100 + 99`
(or `+ 98` for post-drift probes). Stream ID 6 is reserved for probes. The
same exogenous draw for a given synthetic individual/session/window is reused
across conditions. The archetype changes the response law, not the random
initializer. Record these formulas and stream identifiers in the configuration
and manifest.

Neither latent optimum nor oracle score is available to any policy feature,
reward, tuning, or model snapshot. The held-out probe set has its own seed
family and is generated before outcomes are inspected. Probe selections and
oracle labels are never fed to `ObserveOutcome`. If a single profile is run
under multiple conditions, create independent model instances and separate
snapshot stores; no model or telemetry state can leak between conditions or
between synthetic individuals.

Freeze the simulator and the primary analysis before inspecting Study A
results. A development-only smoke run may verify code and runtime, but its
seeds and outcomes must be excluded from confirmatory analysis. Historical
single-session results may motivate this design; they are not independent
validation of it.

## 8. Fixed held-out decision probes

Trajectory performance is affected by prior actions: a policy can reach a
different state and face a different later candidate set. To measure whether
its **learned decision rule** improves, evaluate all policies on an identical,
held-out set of safe decision probes before session 1 and after each completed
session. Construct each probe as a complete valid `PolicyObservation` using a
synthetic preference, safe Temple Pond environment state, valid fresh
physiology, legal phase/timing state, and full candidate/safety context. The
probe distribution is fixed across checkpoints and both LinUCB conditions,
and includes boundary-near states and `NoChange` opportunities. Record its
generation algorithm and seed in the frozen configuration.

Use a **read-only clone** of each policy's versioned model snapshot for probe
scoring. Probe decisions must not alter live decision counts, safety budgets,
the live model, or future random streams. If read-only evaluation cannot be
implemented and verified, do not report the primary endpoint; a trajectory
metric is not an interchangeable substitute. For each probe, enumerate only
production-safe candidates and calculate the existing noise-free one-step
safe-oracle value

`mu(a) = g × [U(e_after(a)) − U(e_before)]`.

Probe pseudo-regret is `max_safe mu(a) − mu(chosen action)`; lower is better.
`NoChange` is included. Use the **same** probe contexts, safe candidate set,
and oracle values for Carry and Reset. A zero-gain probe can yield zero regret
for every action; report how many such probes exist and exclude A9 from H-A1.
For the drift challenge, keep a distinct pre-change and post-change probe bank
so a moving optimum is not averaged into an apparently stable test set.

## 9. Outcomes

### Primary endpoint

For each A1–A7 synthetic individual, average probe pseudo-regret across its
32 held-out contexts after session 20. Compute `Carry − Reset` for that same
individual. The estimand is the mean paired difference, equally weighted
across the seven archetypes. Lower is better. Report the baseline pre-session
probe result beside it to detect initial mismatch.

### Secondary longitudinal outcomes

- Probe pseudo-regret at baseline and after each session, by archetype and
  policy; cumulative valid updates on the x-axis as well as session number.
- Per-session and cumulative **trajectory** safe-oracle pseudo-regret per
  eligible decision, with `N = 0` recorded as missing rather than zero.
- Synthetic continuous-stress change and area under its session curve,
  production reward per valid outcome, HR and RMSSD changes, and final safe
  environment distance from the simulator optimum; label each synthetic.
- Decision opportunities, executed actions, `NoChange`, valid outcomes,
  model updates, skipped decisions, invalidated rewards, snapshot failures,
  and total normalized environment variation per session.
- Safety rejections/modifications, scene-limit and multi-dimension
  violations, reused physiology windows, invalid model updates, pause or
  network boundary violations, and any non-finite model state.
- Carry versus Static and Rule-Based differences in trajectory outcomes,
  reported as exploratory comparisons.
The optional three-consecutive-checkpoint practical-margin endpoint is omitted
from v1.0 because no minimum difference was selected before the smoke run.
Session-by-session probe curves remain descriptive secondary outcomes.

Do not use production reward as the oracle or treat synthetic stress
improvement as an observed participant benefit. The oracle is a property of
the declared generator; production reward is reconstructed from its emitted
and accepted physiology and may rank policies differently.

## 10. Analysis plan

The primary row is one paired profile trajectory, not 20 independent session
rows. Report mean and median paired differences, standard deviation,
distribution/ECDF, 95% confidence interval, and the fraction of profiles on
which Carry has lower probe regret. Weight archetypes equally: first average
the seven archetype-specific differences for each seed index, then summarize
the 30 seed-index contrasts. This preserves the paired common-random-number
design without pretending that 210 archetype trajectories are independent
draws from a human population.

For H-A1 use a two-sided paired sign-flip test on those 30 seed-index
contrasts, with 100,000 deterministic Monte Carlo resamples, add-one p-value,
and `alpha = 0.05`. Also report a 95% percentile paired bootstrap confidence
interval from 10,000 resamples of seed indexes. The frozen configuration
uses PCG32 seed/stream `930001/7` for sign flips and `940001/8` for bootstrap
resampling. The directional hypothesis is supported only if
the paired mean is negative **and** the two-sided test meets the threshold.
Report the estimate and interval regardless of the threshold.

Plot each archetype's mean probe-regret trajectory with uncertainty by
synthetic profile, and a pooled plot with equal archetype weight. Report
early (sessions 1–5), middle (6–15), and late (16–20) trajectory summaries;
these are descriptive, not separately tested hypotheses. For drift, show
regret immediately before and after the switch and subsequent recovery; for
loss, show adaptation availability and updates as well as regret among
eligible decisions. Never silently discard zero-decision sessions from
availability or safety denominators.

Only a harness crash or failed schema/checksum validation excludes a full
trajectory from policy inference. Keep its record and reason. A missing
baseline or all-invalid physiology produces an availability result, not a
fabricated reward or silent exclusion. If one condition of a paired profile
fails, omit that **pair** from H-A1 and rerun all conditions for the profile
only after correcting an implementation fault under a versioned amendment.
Report attempted, complete, incomplete, and excluded profiles by condition.

## 11. Safety and implementation checks before the full run

1. Unit-test snapshot capture/restore, identity rejection, reset isolation,
   and exact update-count continuity across a session boundary.
2. Verify new session state clears pending outcomes and source-window IDs;
   no physiology window contributes reward to two actions or sessions.
3. Verify probe evaluation leaves the live snapshot, counters, future
   decisions, and random streams unchanged; its candidate set matches the
   production safety filter and always permits `NoChange`.
4. Run deterministic boundary/fault cases from the prior invariant suite and
   add model-boundary cases: wrong participant, wrong schema/configuration,
   corrupted/non-finite snapshot, missing snapshot, and interrupted save.
5. Run `python analysis/component-c-bandit-evaluation/scripts/validate_longitudinal_visual_profile_v1.py`
   and reject a missing or mismatched v3 asset, v4 mapping, or scene binding.
   Verify the longitudinal runner loads that pair rather than the historical
   `FrozenEvaluationProfileLoader.TryLoad` default.
6. Compare a one-session Reset run with the historical harness **only after
   giving both the same visual profile**; decisions, rewards, and telemetry
   should then match. A v3-versus-v2 difference is expected and must be
   reported as a configuration difference, not a regression.
7. Validate manifests and every output checksum before analysis. Record code
   commit, clean/dirty status, Unity version, configuration/profile/snapshot
   schema identities, **v3 scene and v4 mapping asset hashes**, generator
   version, seed family, and analysis-script hash.

The full run must use the production core decision, safety, transition,
physiology, and reward logic. An evaluation-only policy-lifetime wrapper may
manage snapshots; it may not bypass the safety path. A zero observed
violation supports only the tested states and simulator faults. It is not a
proof of clinical or Quest runtime safety.

## 12. Output and reporting

Write a new output namespace, for example
`analysis/component-c-bandit-evaluation/longitudinal/`, leaving prior
artifacts untouched. Retain a frozen config, manifest, one row per synthetic
profile/session/condition, one row per probe/checkpoint/action, transition
JSONL or equivalent trace, aggregate tables, and scripts that regenerate
figures. Every row carries protocol ID, run ID, profile ID, archetype,
condition, session index, seed identifiers, policy and feature versions,
snapshot ID/update count, status, and exclusion reason. Profile IDs are
synthetic and must never be joined to Study B participants.

Predefine figure titles and captions so every plot is explicitly labelled
**simulated**. Show the negative-control and unsuccessful cells. Present the
primary estimate before secondary or sensitivity results. Keep Study B
self-report, physiological, usability, and feasibility outcomes in separate
participant-level tables and avoid a pooled simulator-plus-human effect.

## 13. Interpretation and limitations

A favorable Carry result would show learning under the specified synthetic
response laws and frozen production-like pipeline. It would not demonstrate
that real users have those optima, that the reward tracks subjective relief,
or that repeated headset use is acceptable. A poor Carry result is equally
informative given 11 actions, sparse valid rewards, model misspecification,
and potential non-stationarity. The fixed probe set isolates decision-rule
change; it does not itself measure the lived value of an entire VR session.
The simulator's utility function and physiology mapping are researcher
choices, so even paired, reproducible results remain conditional on them.

The prototype is currently single-scene for this evaluation. Do not generalize
Temple Pond findings to Forest Lake, adaptive audio, transport reliability,
human stress reduction, or on-device comfort. Study B should primarily
establish feasibility, acceptability, data completeness, and descriptive
short-term human outcomes, with its own prespecified instruments and safety
procedures.

## 14. Reporting checklist

- State protocol version, freeze date, code/configuration identities, all
  deviations, attempted and completed profile counts, and exact seed range.
- State the `temple-pond-pilot-freeze-v3` visual bundle, normalized action
  steps, six-second transition, v4 raw mapping, and preflight result.
- Report the primary Carry−Reset estimate, interval, p-value, and all
  archetype results, even if the hypothesis fails.
- Report valid-update and decision-availability curves alongside regret.
- Report all specified safety/attribution counts and snapshot failures.
- Separate held-out probe performance from full-trajectory outcomes.
- Identify Carry as evaluation-only until production persistence is approved
  and implemented; state that current feature schema excludes session context.
- Use “synthetic profile/individual,” never “participant,” for Study A.

## 15. Methodological basis

The longitudinal comparison tests a contextual bandit's ability to use
sequential feedback ([Li et al., 2010](https://arxiv.org/abs/1003.0146)).
The separate drift challenge is warranted because a fixed comparator can hide
losses when response distributions change
([Luo et al., 2018](https://arxiv.org/abs/1708.01799)). These sources motivate
the evaluation questions; neither validates this VR simulator or its numerical
parameters.

## 16. Freeze record and remaining governance

On 2026-10-19, the investigator requested that the proposed matrix be frozen
after reporting that EditMode and PlayMode tests passed and completing an
inspected 68-session development smoke run. The investigator explicitly chose
to omit the optional practical-margin endpoint. This v1.0 protocol locks the
matrix, implemented probe-generation algorithm, inferential random streams,
visual v3 calibration, and evaluation-only persistence wrapper. See the
[freeze record](LAMINAR_VR_LONGITUDINAL_STUDY_A_FREEZE_RECORD_v1.0.md)
for the exact evidence and unresolved sign-off.

Supervisor/team approval has not been recorded in this repository. If required
by the research governance process, obtain and record it before treating the
full study as formally pre-registered. If cross-session learning is intended
for real users, separately approve a production ADR for model lifetime,
participant-linked storage, privacy/consent, forgetting, and restore-failure
behavior. No production policy or shared data contract is changed here.
