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
