# Architecture Decisions

Each decision below is traceable to a measured result.

## Inference runs on the backend, not the headset or phone

Quest 2 shares its GPU between rendering and any compute; dropped
frames cause motion sickness. Running inference on the phone would
still require a relay to reach the Quest and website, so it removes
no complexity while duplicating the validated pipeline in another
language.

## Ships the single population CNN, not the ensemble

| Model | Macro F1 |
|---|---|
| Three-way ensemble | 0.715 |
| Population CNN alone | 0.654 |

Difference: dF1 = 0.023, p = 0.5245, d = 0.34 — not significant.
The ensemble also requires a per-user fine-tuning pipeline. Not
worth 3x inference cost for an unproven gain.

## Causal EWMA baseline, not Cosinor

Cosinor fits the whole session; a live device cannot. Measured cost
of going causal:

| Baseline | Macro F1 |
|---|---|
| Offline Cosinor | 0.569 |
| Best single causal tracker | 0.508 |
| Three-scale causal EWMA | 0.557 |

Multi-timescale recovers roughly 80% of the gap.

## Cold start seeds from the population mean

| Cold-start strategy | Macro F1 |
|---|---|
| No baseline | 0.470 |
| Donor-cluster ("borrowed") | 0.475 |
| Population mean | 0.500 |
| Own full baseline | 0.569 |

Donor matching was significantly worse than the subject's own
baseline (p = 0.0026, d = -1.01) and scored below the population
mean. Do not cluster new users into donor groups.

## Confidence gating with merged bands

Among low-confidence windows, 84.2% had the top two classes
adjacent, consistent with the measured physiological overlap between
neighbouring levels (eps-squared 0.03 for levels 2 vs 3). At 80%
coverage: F1 +0.053, severe errors -0.036.

## Open items

- **Baseline maturity thresholds are provisional.** The minimum-window
  experiment was invalidated — Cosinor fits fell back to prefix means
  for 15/15 subjects at short budgets. Re-run with EWMA.
- **60-beat window** scored better than 120 (0.595 vs 0.552) but awaits
  seed replication, and would require retraining.
