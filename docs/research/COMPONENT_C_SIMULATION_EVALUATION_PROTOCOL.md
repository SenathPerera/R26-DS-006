# Component C Simulation Evaluation Protocol

**Protocol ID:** `component-c-simulation-evaluation-v1.2`  
**Status:** Frozen for implementation  
**Freeze date:** 2026-10-13  
**Amendment date:** 2026-10-14, before full-matrix result generation  
**Component:** C — Safety-Constrained Adaptive VR Environment  
**Target system:** Meta Quest 2, Unity 6 LTS, Japanese Temple Pond Garden  
**Paper title:** *Design and Simulation-Based Evaluation of a Safety-Constrained Contextual Bandit for Physiologically Adaptive Virtual-Reality Meditation*

## 1. Purpose

This protocol defines a reproducible, simulation-based evaluation of the
implemented adaptive visual-environment component. It is used because no
participant outcome dataset will be collected for the present paper.

The evaluation addresses three questions that can be answered without human
participants:

1. whether the implementation preserves its specified safety and learning
   invariants;
2. how the production contextual-bandit policy behaves under explicitly
   defined synthetic action-response models; and
3. how physiology faults, sparse feedback, delayed rewards, and model
   misspecification affect that behavior.

The evaluation does **not** establish that the application relaxes people,
improves meditation, is comfortable, prevents cybersickness, or is clinically
safe. Synthetic users are test models, not digital replicas of people.

This protocol supplements rather than replaces
`codex-local/CODEX_BLUEPRINT_ADAPTIVE_VR_MEDITATION.md`. It does not change the
production architecture, action space, reward contract, safety model, session
timing, scene list, or cross-component data contracts.

## 2. Frozen system under evaluation

The primary evaluation uses the active engineering-pilot bundle
`temple-pond-pilot-freeze-v2`. ADR-017 changes only telemetry relative to the
research settings frozen by ADR-016.

| Concern | Frozen value |
|---|---|
| Scene | Japanese Temple Pond Garden (`temple-pond`) |
| Environment dimensions | illumination, warmth, atmospheric softness, color richness, ambient motion |
| Action space | `NoChange` plus increase/decrease for each of the five dimensions |
| Policy feature schema | `adaptive-vr-policy-features/1.0`, 24 ordered features |
| LinUCB model | disjoint LinUCB, one model per action |
| Ridge regularization | `1.00` |
| Exploration coefficient | `0.25` |
| Model lifetime | fresh model for every session |
| Preference initialization | 65% full preference plus 35% scene-safe default |
| Adaptive phase | 900 seconds |
| Decision interval | 75 seconds |
| Expected opportunities | 11, subject to runtime gates |
| Physiology cadence | 60 seconds |
| Physiology window duration | 30 seconds |
| Transition duration | 4.12 seconds |
| Reward settling time | 5 seconds |
| Maximum attribution wait | 120 seconds |
| Maximum same-direction actions | 2 |
| Maximum total normalized variation | 0.90 |
| Reward weights | stress 1.00; RMSSD 0.35; heart rate 0.15; change 0.10; discomfort 0.00; safety 2.00 |
| Telemetry schema | `adaptive-vr-telemetry/1.1-pilot` |

The scene-safe default is fixed as:

\[
d = (0.319,\ 0.500,\ 0.000,\ 0.500,\ 0.750).
\]

The production action steps are:

\[
\Delta = (0.10,\ 0.25,\ 0.30,\ 0.20,\ 0.20).
\]

All applied actions must continue through the production candidate filter,
safety validator, environment parameter manager, and transition controller.
The evaluation must not add a production policy mode or bypass safety to obtain
an ablation result.

## 3. Research questions

### RQ1 — Safety and update integrity

Does the production pipeline prevent unsafe environment execution and model
updates from invalid or incorrectly attributed physiological data across the
specified nominal, boundary, and fault-injection cases?

### RQ2 — Policy performance under synthetic heterogeneity

How do static personalized, rule-based adaptive, random-safe, and
safety-constrained LinUCB policies compare in one-session safe-oracle regret,
synthetic physiological outcomes, production reward, and environment
variation across heterogeneous synthetic response models?

### RQ3 — Sparse-session learning

Within the production-matched 11-opportunity horizon, does LinUCB exhibit
measurable within-session learning, and how frequently does reward timing or
data validity reduce the number of updates below that horizon?

### RQ4 — Robustness

How do noise, missing windows, signal-quality failures, latency, stale data,
out-of-order data, network gaps, delayed reward, and combined faults affect
valid updates, regret, reward, and safety outcomes?

### RQ5 — Model misspecification and non-stationarity

How does LinUCB performance change when the synthetic response is nonlinear,
thresholded, weak, absent, preference-conflicting, or changes during a
session?

### RQ6 — Configuration sensitivity

How sensitive are the simulation results to the LinUCB exploration coefficient
and ridge regularization, without using the result to retroactively alter the
frozen production profile?

## 4. Hypotheses and success criteria

The hypotheses apply only to the declared simulator distribution.

### H1 — Executed safety

Across all primary, robustness, sensitivity, and fault-injection runs:

- executed environments remain within the normalized scene limits;
- an executed non-`NoChange` action changes at most one dimension;
- the consecutive-direction and total-variation limits are not exceeded; and
- pause, emergency, stabilization, invalid signal, stale signal, network loss,
  and active-transition gates prevent prohibited adaptation.

**Acceptance criterion:** zero observed executed violations. A zero count is
evidence for the tested state space, not a mathematical proof for every
possible runtime state.

### H2 — Invalid-update prevention

No rejected, stale, overlapping, reused, non-finite, low-quality, paused,
emergency-crossing, or timed-out outcome updates any policy model.

**Acceptance criterion:** zero invalid model updates and exact agreement
between valid reward count and eligible model-update count for LinUCB.

### H3 — Learning against random-safe selection

For the stationary responsive archetypes A1–A7, production LinUCB has lower
mean per-eligible-decision safe-oracle pseudo-regret than the random-safe
baseline.

This is the only confirmatory policy-performance hypothesis. Comparisons with
the static and rule-based policies are reported as planned exploratory
comparisons because the 11-arm, approximately 11-opportunity horizon may be
too sparse for reliable online superiority.

### H4 — Effect of non-stationarity

Production LinUCB has higher safe-oracle pseudo-regret in archetype A8 than in
the stationary aligned archetype A1 under matched seeds and initial
conditions.

### H5 — Fault tolerance

Increasing fault severity reduces decision or reward availability, but does
not increase executed safety violations or invalid model updates above zero.

Failure to support H3 or H4 remains a valid result. Configuration, scenarios,
or exclusions must not be changed after inspecting results merely to obtain a
favorable policy ranking.

## 5. Evaluation conditions

### 5.1 Production policies

1. **Static Personalized** — holds the shared blended initial state.
2. **Rule-Based Adaptive** — uses the frozen rule profile and moves toward the
   full preference when its activation conditions are met.
3. **Contextual Bandit** — uses the frozen disjoint LinUCB configuration.

### 5.2 Evaluation-only comparator

4. **Random Safe** — samples uniformly from the currently available policy
   candidates, including `NoChange`, and still passes the selected action
   through the production safety validator and transition pipeline.

Random Safe is not a production study condition and must be clearly labelled
as an evaluation comparator in code, output, and the paper.

### 5.3 Safe oracle

The safe oracle is a counterfactual metric, not a deployed policy. At each
decision for which the evaluated policy is eligible to act, it evaluates the
noise-free one-step expected latent response of every action that is valid
under the same scene and safety state. It supplies the comparator value used
to calculate pseudo-regret.

The oracle does not alter the evaluated trajectory and does not receive future
information. When the runtime gates permit no action, the comparator action is
`NoChange` and the opportunity contributes zero regret.

### 5.4 Safety-layer contribution

There will be no executed unconstrained-policy condition. Instead, the harness
will retain each proposal and requested target before final safety validation
and record:

- whether it would exceed a scene or session limit;
- whether it would violate a phase, data, transition, or repetition gate;
- whether the safety layer rejected or modified it; and
- the counterfactual one-step synthetic response of the unexecuted proposal.

This shadow analysis quantifies safety intervention without applying an unsafe
environment state.

## 6. Simulated session timeline

Simulated time must follow the production timing instead of using one abstract
bandit step per loop.

| Interval | Simulated time |
|---|---:|
| Acclimatization | `[0, 120)` seconds |
| Adaptive | `[120, 1020)` seconds |
| Stabilization | `[1020, 1170]` seconds |

Physiology windows end at 60-second intervals and cover the preceding 30
seconds. Adaptive decision opportunities occur at:

\[
195, 270, 345, 420, 495, 570, 645, 720, 795, 870, 945\ \text{seconds}.
\]

An accepted environment change takes 4.12 seconds. Reward attribution observes
the first fresh valid physiology window whose start is after transition
completion plus the 5-second settling interval and whose arrival is within the
120-second attribution limit. This timing can legitimately cause later
decision opportunities to be skipped while a reward remains pending.

Simulation advances by events rather than wall-clock time. No experiment must
wait for a real 20-minute duration.

When events share a timestamp, the harness uses this fixed order: apply a
network-connectivity boundary; complete an active environment transition;
emit and deliver due physiology; resolve any eligible reward; apply a
session-phase boundary; then process a decision opportunity. Therefore the
network is already unavailable for all events at the closed start of a
declared gap and is available at its open end. The window ending at 120
seconds contributes to the
acclimatization baseline, the window ending at 1020 seconds may resolve the
last adaptive reward before stabilization begins, and the windows ending at
420 and 720 seconds are available to their same-time decisions. This ordering
is part of the protocol and must not vary by policy or replication.

## 7. Synthetic response generator

### 7.1 Separation from production reward

The generator produces heart rate, RMSSD, stress probabilities, continuous
stress, timestamps, and signal quality. It must never directly assign the
reward consumed by LinUCB.

The production `PhysiologyStateBuffer`, `RewardAttributionTracker`, and
`RewardCalculator` remain responsible for accepting windows, attributing
outcomes, and calculating model-update reward.

### 7.2 Preferences and initial state

For each replication, the five preference values are sampled independently:

\[
p_j \sim \operatorname{Uniform}(0.20, 0.80).
\]

The restricted interval avoids making most actions unavailable at initialization
while still spanning low and high preferences. The production initialization
is then used without modification:

\[
e_0 = 0.65p + 0.35d.
\]

### 7.3 Latent environment utility

For environment state \(e\), synthetic optimum \(o\), and non-negative weights
\(w\) that sum to one, define per-dimension closeness:

\[
c_j(e,o) = 1 - |e_j-o_j|.
\]

The base latent utility is:

\[
U_{base}(e) = \sum_{j=1}^{5} w_j c_j(e,o)^2.
\]

This latent utility is known only to the simulator and safe-oracle metric. It
is not included in policy observations or production reward.

### 7.4 Physiological dynamics

Initial continuous stress is sampled from
`Uniform(1.70, 2.70)`. Initial heart rate is sampled from
`Uniform(62, 82)` bpm and initial RMSSD from `Uniform(25, 65)` ms. These ranges
are engineering test ranges and are not asserted to describe a clinical or
population distribution.

For each eligible post-settling outcome, let
\(\Delta U = U(e_{after})-U(e_{before})\). Continuous stress evolves as:

\[
s_{k+1}=\operatorname{clip}_{[0,3]}
\left(s_k-b-g\Delta U+\epsilon_s\right),
\]

where the per-opportunity natural recovery
\(b\sim\operatorname{Uniform}(0.02,0.06)\) is sampled once per session, `g` is
the archetype response gain, and nominal
\(\epsilon_s\sim\mathcal{N}(0,0.04^2)\).

Heart rate and RMSSD then evolve from the stress change:

\[
h_{k+1}=\operatorname{clip}_{[45,130]}
\left(h_k+2.5(s_{k+1}-s_k)+\epsilon_h\right),
\]

\[
r_{k+1}=\operatorname{clip}_{[5,150]}
\left(r_k-4.0(s_{k+1}-s_k)+\epsilon_r\right),
\]

with nominal \(\epsilon_h\sim\mathcal{N}(0,0.8^2)\) bpm and
\(\epsilon_r\sim\mathcal{N}(0,1.2^2)\) ms.

The two acclimatization windows use symmetric baseline perturbations with
population standard deviations of 0.10 stress-score points, 2.0 bpm, and 3.0
ms respectively. This prevents a numerical near-zero baseline from dominating
the nominal experiment. Baseline-deviation sensitivity is tested separately.

Continuous stress is converted to the four-class probability vector by linear
interpolation between the two adjacent integer stress levels. The point class
is the maximum-probability class, ties resolve toward the lower stress class,
and confidence is the maximum probability. This conversion is deterministic
and always sums to one before fault injection.

Nominal signal quality is `0.90`. Optional SDNN is absent because it is not
used by the production feature or reward schemas. Missing values must remain
missing rather than being imputed.

### 7.5 Response application timing

An environment-response term is consumed once, by the first physiology window
eligible to represent the action after transition and settling. Natural
recovery and observation noise continue at each physiology emission. A window
must not be reused for two actions. If a response window is rejected or times
out, the internal synthetic physiology may continue evolving, but the policy
must receive no fabricated reward or update.

## 8. Synthetic archetypes

Weights are ordered as illumination, warmth, atmospheric softness, color
richness, and ambient motion.

| ID | Description | Optimum and utility | Weights | Gain `g` |
|---|---|---|---|---:|
| A1 | Preference-aligned balanced | `o = p`, base utility | `(0.20, 0.20, 0.20, 0.20, 0.20)` | 4.00 |
| A2 | Illumination dominant | `o = p`, base utility | `(0.60, 0.10, 0.10, 0.10, 0.10)` | 4.00 |
| A3 | Warmth dominant | `o = p`, base utility | `(0.10, 0.60, 0.10, 0.10, 0.10)` | 4.00 |
| A4 | Softness/richness interaction | `o = p`; `U = 0.5 U_base + 0.5 c_softness c_richness` | `(0.10, 0.10, 0.35, 0.35, 0.10)` | 4.00 |
| A5 | Motion-averse threshold | `o_motion = min(p_motion, 0.20)`; `U = 0.8 U_base + 0.2 I(e_motion <= 0.35)` | `(0.10, 0.10, 0.10, 0.10, 0.60)` | 4.00 |
| A6 | Preference/physiology conflict | warmth and motion optima are `clip(1-p, 0.20, 0.80)`; other optima equal preference | `(0.20, 0.20, 0.20, 0.20, 0.20)` | 4.00 |
| A7 | Weak responder | `o = p`, base utility | `(0.20, 0.20, 0.20, 0.20, 0.20)` | 0.75 |
| A8 | Non-stationary responder | A1 until 570 seconds; then illumination and warmth optima become `clip(1-p, 0.20, 0.80)` | `(0.20, 0.20, 0.20, 0.20, 0.20)` | 4.00 |
| A9 | Physiological non-responder | `o = p`, but environment response is disabled; natural recovery and noise remain | `(0.20, 0.20, 0.20, 0.20, 0.20)` | 0.00 |

The indicator in A5 creates a deliberate threshold discontinuity. A4, A5,
and A8 test violations of a simple stationary linear-response assumption.

## 9. Fault and robustness scenarios

Study B uses A1 and A5 because they represent a smooth balanced response and a
thresholded safety-relevant response. Every scenario retains the production
validation rules.

| ID | Scenario | Frozen manipulation |
|---|---|---|
| R0 | Nominal | Parameters in Section 7 |
| R1 | Reduced noise | All three observation-noise standard deviations multiplied by 0.5 |
| R2 | High noise | All three observation-noise standard deviations multiplied by 2.0 |
| R3 | Mild loss | Independently drop 10% of physiology emissions |
| R4 | Severe loss | Independently drop 30% of physiology emissions |
| R5 | Intermittent poor quality | Every fourth emitted window has signal quality `0.40` |
| R6 | Moderate arrival latency | All windows arrive 30 seconds after their source end time |
| R7 | High arrival latency | All windows arrive 75 seconds after their source end time |
| R8 | Stale burst | Three consecutive adaptive windows receive a source timestamp offset of -120 seconds, beginning at the first emission at or after 420 seconds |
| R9 | Out-of-order pair | The emissions ending at 480 and 540 seconds are delivered in reverse order |
| R10 | Network gap | Network is unavailable on `[420,600)` seconds |
| R11 | Combined moderate | noise multiplier 1.5; 15% drop; 15-second latency; every sixth window quality `0.40` |
| R12 | Combined severe | noise multiplier 2.5; 30% drop; 60-second latency; every fourth window quality `0.40`; network unavailable on `[600,780)` seconds |

Random loss uses a dedicated random stream shared across policy conditions.
Network and deterministic fault schedules are identical across policies.
R9 is implemented by delivering the 540-second window at 540 seconds and
holding the 480-second window until 541 seconds; the one-second offset is only
the deterministic ordering tie-break. Loss and latency manipulations also
apply during acclimatization. If fewer than two baseline windows are accepted
before 120 seconds, no policy controller is created and the resulting
zero-decision session remains an availability outcome rather than an
exclusion.

The separate invariant suite also covers duplicate identifiers, non-finite
measurements, invalid probability sums, too-short windows, future timestamps,
session mismatch, reward-window overlap/reuse, pause, emergency stop,
stabilization, active transition, repeated same-direction actions, total
variation exhaustion, and scene-boundary clipping.

## 10. Experiment matrix

### Study A — Primary policy comparison

- Archetypes: A1–A9.
- Policies: Static Personalized, Rule-Based Adaptive, Contextual Bandit, Random
  Safe.
- Replications: 200 paired seeds per archetype.
- Total sessions: `9 × 4 × 200 = 7,200`.
- Seed range: `100000` through `100199`.

The same seed creates the same preference, initial physiology, natural recovery,
and disturbance streams for all four policies within an archetype.

### Study B — Robustness

- Archetypes: A1 and A5.
- Scenarios: R0–R12.
- Policies: the same four policies.
- Replications: 100 paired seeds per archetype/scenario.
- Total sessions: `2 × 13 × 4 × 100 = 10,400`.
- Seed range: `200000` through `200099`.

R0 is intentionally repeated in Study B so robustness contrasts use an
independent seed family and do not reuse Study A outcomes.

### Study C — LinUCB sensitivity

This study does not retune the production result. It evaluates the fixed
production configuration alongside a predefined grid:

- exploration coefficient: `{0.00, 0.10, 0.25, 0.50}`;
- ridge regularization: `{0.10, 1.00, 10.00}`;
- archetypes: A1, A5, A6, and A8;
- replications: 100 paired seeds;
- seed range: `300000` through `300099`.

Total sensitivity sessions: `4 × 3 × 4 × 100 = 4,800`.

The frozen production cell is exploration `0.25`, ridge `1.00`. No cell may be
renamed as the production policy after seeing results. A later configuration
change would require a new ADR and independent validation.

### Study D — Deterministic safety and attribution suite

The invariant cases in Sections 4 and 9 are executed against all relevant
actions, boundary states, and session phases. These cases are exhaustive over
the discrete action enumeration and enumerated gates, rather than Monte Carlo
replications. Each case records its exact input, proposal, validation outcome,
executed state, reward disposition, and update count.

## 11. Randomization and reproducibility

The harness will implement and version a small platform-independent PRNG
instead of relying on an unspecified Unity or .NET random implementation.
`PCG32` with a documented stream initializer is selected.

Each session derives separate deterministic streams for:

1. participant preference and initial state;
2. physiological observation noise;
3. missing-window schedule;
4. random-safe action selection; and
5. any stochastic fault schedule.

Stream separation prevents one policy's different number of random-safe
draws or accepted windows from shifting its physiology-noise sequence. Seeds
and derived stream identifiers are written to the manifest and per-session
summary.

## 12. Outcomes

### 12.1 Primary algorithmic outcome

The primary outcome is **mean safe-oracle pseudo-regret per eligible decision**:

\[
\bar R = \frac{1}{N_e}\sum_{t=1}^{N_e}
\left(\mu_t(a_t^*)-\mu_t(a_t)\right),
\]

where \(N_e\) is the number of decision opportunities at which the runtime
permits policy selection, \(a_t^*\) is the best one-step safe action under the
noise-free synthetic response, and \(\mu_t\) is expected one-step reduction in
continuous stress. Lower values are better.

For reproducible implementation, the environment-attributable oracle value is
defined exactly as:

\[
\mu_t(a)=g\left[U(e_{after}(a),t)-U(e_{before},t)\right].
\]

Natural recovery is common to every candidate and observation noise has zero
expectation, so neither is included. Candidate ties resolve by the declared
`EnvironmentAction` enumeration order. The oracle uses only the production
safety-filtered candidates available at that decision and does not mutate the
simulator or policy.

If `N_e = 0`, the metric is missing rather than zero, and the run remains in
availability and safety summaries.

### 12.2 Secondary algorithmic outcomes

- cumulative safe-oracle pseudo-regret;
- cumulative production reward;
- mean production reward per valid outcome;
- first-to-final-third change in mean pseudo-regret;
- simulated continuous-stress change from acclimatization baseline;
- area under the simulated stress-score curve;
- heart-rate and RMSSD changes, explicitly labelled synthetic;
- count of valid decisions, executed changes, valid outcomes, and model
  updates;
- first opportunity at which LinUCB's rolling regret is below its first-third
  mean, when it occurs;
- selected-action distribution and action entropy; and
- time and observations to the best visited synthetic environment state.

### 12.3 Safety and robustness outcomes

- executed scene-limit violations;
- executed multi-dimension action violations;
- executed repetition or total-variation violations;
- unsafe proposals rejected or modified;
- invalid rewards reaching a policy update;
- reused post-action physiology windows;
- decisions skipped by reason;
- rewards invalidated by reason;
- `NoChange` frequency;
- total normalized environment variation; and
- degradation from matched nominal performance.

### 12.4 Technical outcomes

- policy-selection duration;
- complete decision/reward cycle duration in accelerated simulation;
- peak managed allocation where the Unity profiler or supported measurement
  API can measure it reliably;
- telemetry event completeness and sequence continuity; and
- output-manifest validation.

Editor timing is not reported as Quest 2 frame-time evidence.

## 13. Statistical analysis

### 13.1 Unit of analysis

The unit of analysis is one complete simulated session. Pairing is by
archetype/scenario and seed. Individual decision transitions are not treated as
independent samples for policy-level inference.

### 13.2 Descriptive reporting

For every policy and scenario, report:

- number of attempted and valid sessions;
- mean, standard deviation, median, and interquartile range;
- 95% paired-bootstrap confidence intervals for policy contrasts;
- raw counts for safety and validity outcomes; and
- the full distribution or an empirical cumulative distribution for the
  primary outcome.

Confidence intervals use 10,000 deterministic paired bootstrap resamples.
The mean-difference confidence interval uses the protocol PCG32 implementation
with seed `910001` and
stream identifier `0x4253545250414952`. Descriptive quartiles and percentile
confidence limits use Hyndman-Fan type 7 interpolation. Standard deviation is
the sample standard deviation with denominator `n-1`. Probability of
superiority is the paired proportion for which the left/lower-regret condition
wins, with ties contributing one half.

For first-to-final-third regret, each third contains `floor(N_e/3)` eligible
decisions; any remainder stays in the middle segment. The rolling-regret
diagnostic uses the same trailing-window width. Action entropy is Shannon
entropy in natural-log units. Stress-score AUC uses trapezoidal integration
over accepted source-window end timestamps.

### 13.3 Confirmatory tests

- **H3:** two-sided paired permutation test comparing LinUCB with Random Safe
  on mean safe-oracle pseudo-regret over A1–A7. The paired effect is first
  averaged across those archetypes for each seed.
- **H4:** two-sided paired permutation test comparing LinUCB outcomes for A8
  and A1 under the same seeds. The outcome is mean safe-oracle pseudo-regret.
- Statistical threshold: `alpha = 0.05`.

Both confirmatory tests use 100,000 deterministic Monte Carlo paired
sign-flip resamples. The test statistic is the absolute paired mean
difference. Resampling uses the protocol PCG32 implementation with seed
`920001` and stream identifier `0x50524D5041495232`. The reported two-sided
p-value is `(b + 1) / (B + 1)`, where `b` is the number of resampled absolute
statistics at least as large as the observed absolute statistic and `B` is
100,000. Zero paired differences remain in the sample. H3 and H4 retain their
predeclared individual `alpha = 0.05` thresholds; only exploratory secondary
families receive multiplicity adjustment.

H1, H2, and H5 are evaluated from invariant counts and fault-severity summaries,
not null-hypothesis tests. Secondary pairwise policy comparisons use paired
permutation tests with Holm correction within each outcome family.

Alongside p-values, report paired median difference, paired mean difference,
95% confidence interval, and probability of superiority. Statistical
inference applies only to the frozen synthetic generator and seed distribution;
it must not be described as inference about people.

### 13.4 Monte Carlo precision

The fixed 200 primary replications and 100 robustness/sensitivity replications
are selected to stabilize distribution estimates while keeping the exact
production pipeline practical to execute. A convergence diagnostic will show
the primary contrast at cumulative replication counts 25, 50, 100, and 200.
The final estimate remains the predeclared 200-replication result regardless
of whether an earlier estimate appears favorable.

## 14. Exclusions and missing results

A simulated session is excluded from policy-performance analysis only when:

- the harness crashes;
- output fails schema or checksum validation;
- the loaded production profile differs from the frozen identity; or
- an implementation defect makes the session uninterpretable.

Policy decisions, zero-update sessions, safety rejections, missing rewards,
and physiology failures are outcomes, not exclusion reasons.

Every exclusion is retained in an audit table with seed, configuration,
reason, and disposition. If a shared harness defect is fixed, all affected
policies and seeds are rerun, not only the unfavorable condition. The code and
protocol version must change when a fix changes experiment semantics.

## 15. Output and provenance

The planned artifact structure is:

```text
analysis/component-c-bandit-evaluation/
├── README.md
├── configs/
├── scripts/
├── manifests/
├── results/
│   ├── session-summaries/
│   ├── aggregate-tables/
│   └── figures/
└── schemas/
```

Each run manifest records:

- protocol ID and content hash;
- Git commit and dirty-worktree indicator;
- Unity, application, feature-schema, telemetry-schema, policy, reward,
  physiology, timing, scene, preference, and safety configuration identities;
- experiment, archetype, scenario, policy, and seed;
- PRNG and stream version;
- start/end timestamps and run status;
- result counts and output checksums; and
- operating system and non-identifying runtime information.

Transition-level outputs use JSON Lines. Session summaries and aggregate tables
use invariant-culture CSV. Plotting scripts consume the summaries and must not
manually transcribe values into figures.

Generated raw results may be stored outside Git when large, but the repository
must retain the protocol, configuration, schemas, execution scripts, aggregate
tables, figures, and enough representative output to audit the pipeline.

## 16. Public physiological datasets

Public physiological datasets are excluded from the confirmatory evaluation.
WESAD and SWELL-KW contain useful stress-related physiology but do not contain
the randomized Temple Pond actions, logging propensities, or action-attributed
outcomes required to evaluate this policy. Treating their natural changes as
responses to this VR action space would fabricate counterfactual evidence.

They may be discussed in related work or used later for a separately labelled
physiology-ingestion robustness analysis. They must not parameterize action
effects or be presented as validation of adaptive VR efficacy in this paper.

## 17. Non-participant technical validation

The simulation study is supplemented by:

- a fresh machine-readable Unity EditMode result;
- a fresh machine-readable Unity PlayMode result;
- Android ARM64/IL2CPP build evidence;
- a full-duration Quest 2 soak test;
- Quest frame-time and thermal captures;
- network-loss and reconnection validation;
- disconnected local emergency-stop validation; and
- local telemetry persistence and restart validation.

These activities evaluate software and device operation. They do not measure
comfort, preference, relaxation, presence, or cybersickness. Any person who
wears the headset as part of formal data collection would require the project's
applicable ethics and consent process; informal developer operation is not
converted into participant evidence.

## 18. Paper claims

### 18.1 Permitted claims when supported by results

- The system implements an interpretable safety-constrained disjoint LinUCB
  policy over five normalized visual dimensions.
- The tested pipeline preserved specified software safety and update
  invariants in the enumerated tests and simulations.
- Under named synthetic response models, one policy produced the reported
  reward, regret, availability, and variation outcomes.
- The evaluation identifies consequences of sparse feedback, delayed reward,
  invalid physiology, non-stationarity, and safety intervention.
- Quest tests demonstrate the specific measured build, runtime, networking,
  persistence, or performance behavior that was actually observed.

### 18.2 Prohibited claims

- The system reduces human stress or improves meditation.
- LinUCB is superior for real users.
- Synthetic profiles represent a participant population.
- The application is comfortable, cybersickness-free, clinically effective,
  or medically safe.
- Engineering thresholds or reward weights are scientifically optimal.
- Passing Editor tests proves Quest 2 performance.

The conclusion must use the phrase **simulation-based evidence** and identify
a controlled participant study as necessary future work.

## 19. Threats to validity

The paper must discuss at least:

- **construct validity:** the synthetic utility and production reward are
  proxies, not direct measurements of relaxation;
- **internal validity:** conclusions depend on generator equations, parameter
  ranges, timeline fidelity, and correct reuse of production code;
- **external validity:** no inference to people, other scenes, other headsets,
  or clinical settings is supported;
- **algorithmic validity:** disjoint LinUCB receives very few observations per
  arm in one session;
- **model misspecification:** linear action-value estimation may not fit the
  nonlinear, thresholded, or changing response families;
- **measurement validity:** HR, RMSSD, and continuous stress are simulated and
  correlated by construction;
- **baseline sensitivity:** two acclimatization samples provide a fragile
  normalization estimate despite the configured numerical floor; and
- **researcher degrees of freedom:** archetypes, seeds, metrics, and exclusions
  are frozen here before result inspection.

## 20. Change control

This document freezes the evaluation design for implementation. Editorial
clarifications that do not affect generated data may increment a document
revision note. Any change to an equation, parameter, seed range, experiment
matrix, primary outcome, hypothesis, exclusion rule, or statistical method
requires:

1. a protocol version increment;
2. a dated amendment describing the reason;
3. confirmation of whether prior results had been inspected;
4. regeneration of all affected conditions; and
5. explicit disclosure in the paper.

Production research profiles remain governed by their ADRs. Simulation
findings alone do not authorize silently changing the participant-facing
configuration.

### Amendment v1.1 — 2026-10-14

Before any full Study A or Study B matrix was generated, Phase 5 implementation
identified underspecified analysis details. Version 1.1 freezes the
environment-attributable safe-oracle equation, deterministic bootstrap seed and
stream, type-7 quantiles, sample standard deviation, third/rolling-window
partition, entropy units, and AUC integration rule. No prior full-matrix results
were inspected. All future generated results must carry the v1.1 protocol ID;
no v1 results are retained as research outcomes.

### Amendment v1.2 — 2026-10-14

Before any full Study A or Study B matrix was generated, Phase 6
implementation identified that the paired-permutation procedure was not
computationally or statistically specified. Version 1.2 freezes the H4 outcome
as mean safe-oracle pseudo-regret and freezes the deterministic Monte Carlo
paired sign-flip statistic, resample count, seed, PCG32 stream, add-one
p-value correction, zero-difference handling, and confirmatory multiplicity
interpretation. No prior full-matrix results were inspected. All future
generated results must carry the v1.2 protocol ID; no v1.1 results are retained
as research outcomes.

## 21. Planned paper structure

1. Introduction and research gap.
2. Related work.
3. System and safety requirements.
4. Contextual-bandit formulation and production architecture.
5. Synthetic evaluation methodology.
6. Software and Quest technical validation.
7. Results.
8. Discussion and threats to validity.
9. Proposed future participant study.
10. Conclusion.

## 22. References informing the methodology

- Li, L., Chu, W., Langford, J., and Schapire, R. E. *A
  Contextual-Bandit Approach to Personalized News Article Recommendation*.
  WWW, 2010. <https://arxiv.org/abs/1003.0146>
- Li, L., Chu, W., Langford, J., and Wang, X. *Unbiased Offline Evaluation of
  Contextual-bandit-based News Article Recommendation Algorithms*. WSDM,
  2011. <https://arxiv.org/abs/1003.5956>
- Peffers, K., Tuunanen, T., Rothenberger, M. A., and Chatterjee, S. *A Design
  Science Research Methodology for Information Systems Research*. Journal of
  Management Information Systems, 24(3), 45–77, 2007.
  <https://doi.org/10.2753/MIS0742-1222240302>
- Gottesman, O. et al. *Guidelines for Reinforcement Learning in Healthcare*.
  Nature Medicine, 25, 16–18, 2019.
  <https://doi.org/10.1038/s41591-018-0310-5>
- Schmidt, P., Reiss, A., Duerichen, R., Marberger, C., and Van Laerhoven, K.
  *Introducing WESAD, a Multimodal Dataset for Wearable Stress and Affect
  Detection*. ICMI, 2018.
  <https://ubi29.informatik.uni-siegen.de/usi/data_wesad.html>

## 23. Supervisor-style approval decision

The protocol is approved for implementation as a final-year component study
under the following interpretation:

- the primary contribution is the design and verification of a
  safety-constrained adaptive VR control artifact;
- simulation evaluates algorithm behavior and limitations, not human efficacy;
- negative or inconclusive policy-performance findings are acceptable and
  must be reported;
- the production-matched sparse horizon must remain the primary horizon;
- longer horizons, cross-session persistence, or tuned configurations may be
  reported only as clearly separated future or sensitivity analyses; and
- the manuscript must retain the limitations and prohibited-claim boundaries
  in this protocol.

