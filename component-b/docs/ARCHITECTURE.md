# Architecture Decisions

Every number below traces to an executed notebook cell. Numbers that could not be
traced are marked **[UNVERIFIED]** and must not be reused until re-measured.

**Revision note.** All figures predating `notebook-deployment-decision.ipynb` were
measured under midpoint labeling and were inflated by roughly 0.07–0.08 macro-F1.
They have been replaced. Do not restore them from git history.

## 1. Inference Runs on the Backend, Not the Headset or Phone

Quest 2 shares its GPU between VR rendering and compute; dropped frames cause
motion sickness. Running inference on the phone would still need a relay to reach
the Quest and website, so it adds no simplification while duplicating the
validated Python pipeline in another language.

```text
Wearable (PPG + TMP117)
      |  BLE
      v
  Mobile app        relays raw PPG, runs no model
      |  WebSocket
      v
  Python backend      <- ALL causal feature extraction & inference happens here
      |  WebSocket
   +--+--+
   v     v
 Quest  Website
```

## 2. Labeling: Endpoint, Not Midpoint

Each window is labeled at its **last beat** (`y = labels[e-1]`), and the
time-of-day feature index follows the label.

The pipeline previously labeled at the window centre (`labels[mid]`). A model
trained that way predicts a moment that 30 beats of its own input postdate — it
cannot run live, because those beats have not happened yet.

Measured cost of the correction, 5 seeds per configuration
(`notebook-deployment-decision.ipynb`, 40 runs):

| Configuration | midpoint − endpoint F1 | midpoint − endpoint κ |
| --- | --- | --- |
| XGBoost alone | +0.084 | +0.075 |
| MS-CGCA 2-way | +0.071 | +0.034 |
| BiLSTM 2-way | +0.076 | +0.034 |
| MS-CGCA 3-way | +0.079 | +0.042 |

The inflation is consistent across architectures, so it is a property of the
labeling scheme rather than of any model.

## 3. Model: 60-Beat Causal MS-CGCA 2-Way Ensemble

XGBoost over 25 engineered features, blended with a population Multi-Scale
Circadian-Guided Cross-Attention network over the raw beat sequence. 60-beat
windows (~45 s latency), past-only causal EWMA baselines.

### Configuration comparison (endpoint labeling, 5 seeds, identical eval windows)

| Configuration | Macro F1 | Quadratic κ |
| --- | --- | --- |
| BiLSTM 2-way | 0.5993 ± 0.0120 | 0.7897 ± 0.0232 |
| MS-CGCA 3-way | 0.5991 ± 0.0109 | 0.7773 ± 0.0225 |
| **MS-CGCA 2-way (shipped)** | **0.5925 ± 0.0129** | **0.7755 ± 0.0174** |
| XGBoost alone | 0.5703 ± 0.0040 | 0.7229 ± 0.0036 |

**Why 2-way over 3-way.** The personalised third member adds +0.0066 F1,
Wilcoxon p = 0.625 — indistinguishable from seed noise. It costs per-user
calibration buffering, runtime fine-tuning, and a third artifact, and the 2-way
path must be implemented regardless for cold start. Under the old midpoint
labeling it appeared to add +0.031; that gain was an artifact.

**Why MS-CGCA over BiLSTM.** BiLSTM scores 0.0068 F1 higher (p = 0.625, not a
real difference) and leads on κ by 0.014, also inside one SD. MS-CGCA is selected
on causality grounds: it is causal by construction, whereas BiLSTM is admissible
only *because* the label sits at the window's end and would silently leak future
data again if the windowing changed.

**Why not XGBoost alone.** It trails the sequence models by 0.022–0.030 F1 and
~0.05 κ at p = 0.0625 — the floor for n = 5 paired seeds, and the only gap in the
comparison larger than seed variance.

### Shipped artifact (`notebook-train-export-2way.ipynb`, single seed)

Blend weight selected by pooled grid search over 17 points (0.10–0.90, step 0.05);
performance recorded from nested per-fold selection.

| Metric | Value |
| --- | --- |
| Blend weight | `w_xgb = 0.20`, `w_cnn = 0.80` |
| Macro F1 (nested) | 0.5970 |
| Quadratic κ (nested) | 0.7811 |
| Accuracy | 0.8354 |
| Severe errors (\|e\| ≥ 2) | 0.0535 |
| Within-1 accuracy | 0.9465 |
| Evaluation windows | 12,026 |

Falls within 0.35 SD of the 5-seed estimate above — no pipeline drift.

Selection bias (pooled − nested) measured at +0.0000. **[UNVERIFIED]** — the
per-fold weight distribution has not been printed; confirm all 15 folds select
(0.20, 0.80) before treating the zero as measured rather than coincidental.

### Artifact fingerprints (SHA-256, first 16 hex)

```
models/mscgca_population.keras    1c6d84fd0af0c1d5
models/xgb_population.json        2a801f18dd6a4b47
scalers/feature_scaler.pkl        95dcfe74685280f9
config/model_config.json          f396b0407edaaa9e
```

If a file on disk does not match, it is not the artifact these numbers describe.

### Reference: offline, non-causal

The non-causal 120-beat offline model reaches macro F1 = 0.682, κ = 0.855
(`notebooks/01_pipeline/notebook-improvements.ipynb` cell 14). The deployable
pipeline reaches ≈0.597.

**The causal pipeline does not recover offline performance.** The ~0.085 F1 gap
is the measured cost of past-only features plus endpoint labeling, and is
reported as such.

### MS-CGCA network

1. **Multi-scale causal convolutions** — three parallel `Conv1D(32, 3)` branches
   at dilation 1, 2, 4, `padding='causal'`. Beat-to-beat, short-trend and
   window-level structure without lookahead.
2. **Unidirectional `LSTM(128)`** — produces keys and values. Not bidirectional.
3. **Circadian-guided cross-attention** — the 7-dim circadian/baseline vector is
   projected to `Dense(128)`, repeated, and used as the attention *query* over the
   LSTM sequence. Not concatenated at the output.
4. Global average pooling, concatenated with `Dense(32)` of the circadian vector,
   then `Dense(64)` → `Dense(4, softmax)`.

## 4. Causal EWMA Baseline Engine

Whole-session Cosinor fitting needs future samples and cannot run on a live
stream. Replaced with past-only multi-timescale EWMA:

- **Fast (τ = 60 beats)** — rapid autonomic shifts
- **Medium (τ = 300 beats)** — primary within-session reference
- **Slow (τ = 1800 beats)** — long-term drift

`causal_zscore`, `roll_rmssd_causal` and residual calculations all evaluate on
past-only buffers. Verified in `tests/test_causality.py` by corrupting future
samples and asserting past output is unchanged.

## 5. Cold-Start Seeding from the Population Mean

A new user's EWMA baseline seeds from `POPULATION_RR_MS = 780.0` ms and updates
as beats arrive. Do not cluster new users into donor groups.

**[UNVERIFIED]** The supporting figures (no-baseline 0.470, donor-cluster 0.475,
population mean 0.500, own-baseline ceiling 0.569) cite
`Component_B_Research_Explained.docx`, which is not in this repo, and were
measured under midpoint labeling. Re-measure before quoting.

## 6. Confidence Gating & Merged Band Output

When the classification margin falls below `CONFIDENCE_TAU`, the backend emits a
**merged band** rather than forcing a single label.

The wire format carries the full probability distribution alongside the decision:

```json
{
  "mode": "band",
  "level_low": 1,
  "level_high": 2,
  "label": "mild-to-moderate",
  "confidence": 0.54,
  "adjacent": true,
  "probabilities": {"relaxed": 0.08, "mild": 0.36, "moderate": 0.54, "high": 0.02},
  "timestamp": 1787282898.4
}
```

`mode`, `level`/`level_low`/`level_high` and `label` are the authoritative
decision. `probabilities` is supplementary. **Consumers must not re-derive a
label by taking argmax of `probabilities`** — doing so bypasses the confidence
gate and reintroduces the false precision the band exists to prevent.

`timestamp` is POSIX seconds as a float, matching `StressPrediction.timestamp`.
It is the time of the window's **last** beat — the moment being predicted (§2).

**[UNVERIFIED]** The 80%-coverage tradeoff (F1 +0.053, severe errors −0.036) and
the 84.2% adjacent-error figure are midpoint-derived and uncited. Re-measure
before quoting.

## 7. Open Items

- **Export verification output not recorded.** `max |p_saved − p_memory|` and
  argmax agreement from the export notebook's reload check have not been captured
  in this document. Record them.
- **`model_config.json` contents not recorded here.** Paste the exported file.
- **Per-fold blend weight distribution** — see §3.
- **Zero-shot sensor transfer** — WESAD (chest ECG) → Empatica (wrist PPG)
  collapses to F1 = 0.135, κ = −0.104. **[UNVERIFIED]**, and unrelated to the
  cross-dataset *mechanism* replication in the research paper, which succeeded.
