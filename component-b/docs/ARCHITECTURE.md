# Architecture Decisions

Each decision below is traceable to a measured result. Where a number could not be
traced to a specific notebook cell, it has been removed rather than left unsourced.

## Inference runs on the backend, not the headset or phone

Quest 2 shares its GPU between rendering and any compute; dropped frames cause
motion sickness. Running inference on the phone would still require a relay to
reach the Quest and website, so it removes no complexity while duplicating the
validated pipeline in another language.

```
Wearable (PPG + TMP117)
      |  BLE
      v
  Mobile app          relays raw PPG, runs no model
      |  WebSocket
      v
  Python backend      <- ALL inference happens here
      |  WebSocket
   +--+--+
   v     v
 Quest  Website
```

## Ships the two-way ensemble (XGBoost + population CNN)

| Model | Macro F1 | κ | Source |
|---|---|---|---|
| XGBoost alone | 0.647 | — | `Notebook_Improvements.ipynb` |
| Population CNN-BiLSTM-Attention alone | 0.654 | — | `Notebook_Improvements.ipynb` |
| **Two-way ensemble (weights 0.45 / 0.55)** | **0.682** | **0.855** | cell 14, computed |
| Three-way ensemble | 0.715 | 0.855 | cell 21 — **rejected, see below** |

The deployed system runs **both models** — XGBoost and the CNN-BiLSTM-Attention
network — and blends their output probabilities at approximately 0.45/0.55. This
is the two-way ensemble reported in the paper (§IV-E). The CNN-BiLSTM-Attention
architecture is not dropped; it is one of the two components actually shipped.

A note on 0.687: an earlier notebook cell (`Notebook_Improvements.ipynb`, cell 23,
summary table) prints "0.687" as a baseline comparison figure, but this value is a
**hardcoded constant**, not a computed result — no cell in the notebook derives it.
Do not cite 0.687. The computed two-way baseline is **0.682** (cell 14).

### Why the three-way ensemble was rejected

The three-way ensemble adds a *third* model — a per-subject fine-tuned copy of the
population CNN — as an additional voter (weights xgb=0.28 / cnn=0.42 / ft=0.30).
It was tested and rejected for two independent, sourced reasons:

1. **The reported figure did not reproduce.** Repeated executions of the
   identical pipeline gave different results: a naive re-run (same
   weight-selection procedure as the original) returned macro-F1 0.6889, not
   0.715, with different weights (ft=0.30 / xgb=0.49 / cnn=0.21). A properly
   nested re-run — in which ensemble weights for each held-out subject are
   chosen using only the other fourteen subjects — returned macro-F1 0.6822.
   Across three separate executions, naive F1 ranged 0.673–0.689 and nested F1
   ranged 0.666–0.682, while the originally reported 0.715 never reappeared.
   This indicates 0.715 reflected favourable training-run variance rather than
   a stable property of the architecture, not a number that can be reproduced
   on demand.

2. **Even at its best measured value, the three-way ensemble does not
   meaningfully beat its own strongest single component.** Per-subject mean F1
   for the three models individually: XGBoost 0.591, population CNN 0.573,
   personalised CNN 0.672. The nested three-way ensemble reached 0.682 —
   a gain of roughly +0.01 over the personalised CNN alone, well within the
   measured reseeding noise band of ±0.03.

Combining three models for a gain that is not reliably distinguishable from noise
does not justify tripling inference cost or maintaining a per-user fine-tuning
pipeline. **No significance test for the three-way-vs-single comparison is cited
here** — an earlier draft of this document stated "p = 0.5245" for this
comparison; no cell in any notebook computes that figure, and it has been
removed rather than re-used. If a sourced significance test is added later, it
should replace this paragraph with the correct citation.

## Causal EWMA baseline, not Cosinor

Cosinor fits the whole session; a live device cannot. Measured cost of going
causal:

| Baseline | Macro F1 |
|---|---|
| Offline Cosinor | 0.569 |
| Best single causal tracker | 0.508 |
| Three-scale causal EWMA | 0.557 |

Multi-timescale recovers roughly 80% of the gap. The deployed baseline engine
tracks three EWMA timescales concurrently rather than fitting Cosinor online.

## Cold start seeds from the population mean

| Cold-start strategy | Macro F1 |
|---|---|
| No baseline | 0.470 |
| Donor-cluster ("borrowed") | 0.475 |
| Population mean | 0.500 |
| Own full baseline | 0.569 |

Donor matching was significantly worse than the subject's own baseline
(p = 0.0026, d = -1.01) and scored *below* the population mean. **Do not cluster
new users into donor groups.** A new user's baseline engine seeds from the
population mean and updates causally from there.

## Confidence gating with merged bands

Among low-confidence windows, 84.2% had the top two classes adjacent, consistent
with the measured physiological overlap between neighbouring levels (eps-squared
0.03 for levels 2 vs 3). At 80% coverage: F1 +0.053, severe errors -0.036.

Below the confidence threshold, the system emits a merged band (e.g.
"mild-to-moderate") rather than forcing a single label.

## Open items

- **Baseline maturity thresholds are provisional.** The minimum-window experiment
  was invalidated — Cosinor fits fell back to prefix means for 15/15 subjects at
  short budgets. Re-run with EWMA.
- **60-beat window** scored better than 120 (0.595 vs 0.552) but awaits seed
  replication, and would require retraining.
- **Three-way ensemble weight instability.** Across repeated executions, the
  grid-selected ensemble weights for the (now-rejected) three-way configuration
  varied between runs (e.g. XGBoost weight ranging 0.42–0.49), indicating the
  "optimal" configuration reflects training stochasticity rather than a
  discoverable stable optimum. Documented here in case the three-way approach
  is revisited.
