# Component C simulation evaluation

This directory contains the reproducible analysis boundary for protocol
`component-c-simulation-evaluation-v1.2`. It is a software/simulation study;
its results must not be described as clinical evidence or evidence about human
participants.

The Unity Editor-only evaluation assembly executes the production policy,
safety, transition, physiology-validation, reward, and telemetry paths. It
writes transition events as JSON Lines and session, aggregate, and inferential
tables as invariant-culture CSV. Every completed run has a manifest containing
configuration identities, environment provenance, counts, and SHA-256 output
checksums.

Directory roles:

- `configs/`: frozen machine-readable study and statistical settings.
- `scripts/`: dependency-free artifact validation and later plotting scripts.
- `schemas/`: manifests, transition-event, invariant-case, and tabular output
  schemas.
- `manifests/`: completed run manifests.
- `results/session-summaries/`: one row per simulated session.
- `results/aggregate-tables/`: descriptive and inferential results.
- `results/transitions/`: large reproducible JSONL logs; ignored by Git by
  default.
- `results/invariant-cases/`: exact inputs and outcomes from the deterministic
  Study D safety and attribution catalog.
- `results/figures/`: figures generated from session summaries.

Validate a completed run from the repository root:

```powershell
python analysis/component-c-bandit-evaluation/scripts/validate_artifacts.py `
  analysis/component-c-bandit-evaluation/manifests/<run-id>.json
```

The validator recalculates every declared SHA-256 checksum, parses every JSONL
event, checks CSV row widths and protocol identities, and reconciles manifest
session and transition counts. A full Study A run must also contain complete
H3 and H4 inference rows. No result file should be edited manually.

## Running the frozen studies

After activating the pinned Unity 6 Editor license, open the project and use:

- `Tools > Adaptive Meditation > Research > Full Evaluation > Run Study A`
- `Tools > Adaptive Meditation > Research > Full Evaluation > Run Study B`
- `Tools > Adaptive Meditation > Research > Full Evaluation > Run Study C`
- `Tools > Adaptive Meditation > Research > Full Evaluation > Run Study D`

The commands load and verify the frozen production profiles before starting.
Execution occurs away from the Editor main thread and exposes a cancellable
progress bar. Completed, cancelled, and failed runs receive distinct manifest
statuses; partial outputs are retained for audit and must not be included in
policy-performance inference.

Run each study separately and validate each completed manifest before analysis.
Study C is a predeclared sensitivity analysis and must not be used to rename a
better-performing cell as the production policy. Study D records 206
deterministic cases and is valid only when every case passes. A dirty Git
worktree is recorded transparently, but a clean committed research state is
strongly preferred for final paper results.

Generate deterministic SVG figures from a validated Study A result:

```powershell
python analysis/component-c-bandit-evaluation/scripts/generate_figures.py `
  --summary analysis/component-c-bandit-evaluation/results/session-summaries/<study-a-run>.csv `
  --inference analysis/component-c-bandit-evaluation/results/aggregate-tables/<study-a-run>-inference.csv `
  --convergence analysis/component-c-bandit-evaluation/results/aggregate-tables/<study-a-run>-convergence.csv `
  --output-dir analysis/component-c-bandit-evaluation/results/figures/<study-a-run>
```

The figure generator uses only Python’s standard library and produces an ECDF,
confirmatory-effect plot, convergence plot, and a source/output checksum
manifest. Figures are always derived from CSV outputs rather than manually
transcribed values.

## Frozen longitudinal Study A v1.0 (Temple Pond visual v3)

This is a separate, investigator-frozen 20-session-per-profile simulation. The
historical single-session commands above still use their frozen v2 environment.
Read the [v1.0 longitudinal protocol](../../docs/research/LAMINAR_VR_LONGITUDINAL_SIMULATION_PROTOCOL_v1.0.md)
and [freeze record](../../docs/research/LAMINAR_VR_LONGITUDINAL_STUDY_A_FREEZE_RECORD_v1.0.md)
before the full run. Supervisor/team sign-off remains unrecorded.

From the repository root, verify the stronger v3 scene and v4 mapping:

```powershell
python analysis/component-c-bandit-evaluation/scripts/validate_longitudinal_visual_profile_v1.py
```

Open `unity/` in Unity `6000.0.82f1` and wait for scripts to compile. In the
Editor choose `Tools > Adaptive Meditation > Research > Longitudinal Study A >
Run Smoke (68 sessions)`. The smoke covers every condition and challenge with
one profile, two sessions, and four probes per checkpoint. Inspect Console for
the output directory and run the analyzer below. Its development seeds
`510000` and `520000` are separate from the full-study seeds.

```powershell
$run = Get-ChildItem 'analysis/component-c-bandit-evaluation/longitudinal/runs' `
  -Directory -Filter '*-longitudinal-smoke' |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1
python analysis/component-c-bandit-evaluation/scripts/analyze_longitudinal_v1.py $run.FullName
```

The analysis command verifies status, checksums, row counts, and complete
trajectories, then writes `analysis.json` and `learning-curve.csv` in that run
directory. It omits primary inference for smoke runs.

The completed v0.2 smoke run from before the freeze must be analyzed with
`analyze_longitudinal.py`; the v1.0 analyzer accepts only v1.0 manifests. For a
new v1.0 smoke run, use the v1.0 analyzer above.

Choose `Run Frozen Full Study (20,400 sessions)` from the same menu. Then run:

```powershell
$run = Get-ChildItem 'analysis/component-c-bandit-evaluation/longitudinal/runs' `
  -Directory -Filter '*-longitudinal-full' |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1
python analysis/component-c-bandit-evaluation/scripts/analyze_longitudinal_v1.py $run.FullName
```

The full frozen matrix contains 7 archetypes × 30 seeds × 4 conditions × 20
sessions, plus 3 challenge scenarios × 30 seeds × 2 conditions × 20 sessions.
Each trajectory has 32 fixed held-out probes at baseline and after every
session. The full analysis reports the paired Carry−Reset endpoint,
its bootstrap interval, and a sign-flip p-value in `analysis.json`, with the
30 seed-index contrasts in `paired-index-effects.csv`. A v1.0 run is labelled
`investigator_frozen`; v0.2 runs remain `proposed_not_frozen`.

Generated runs live under `longitudinal/runs/` and are ignored by Git. Each
contains a manifest and session, probe, decision, reward, and synthetic
physiology CSV traces. Cancel the Editor progress dialog to retain a partial
run with `cancelled` status; it cannot enter the analysis. The runner uses the
production decision, safety, transition, validation, and reward pipeline, but
model persistence is evaluation-only. It does not require a Quest headset or
change the deployed policy.
