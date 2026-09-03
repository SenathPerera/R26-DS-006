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
Wearable (PPG + optional temperature)
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
performance recorded from nested per-fold selection. Every figure below comes from
one export and is reproduced from that run's `loso_folds.npz` — do not mix rows
from different exports.

| Metric | Value |
| --- | --- |
| Blend weight | `w_xgb = 0.15`, `w_cnn = 0.85` |
| Macro F1 (nested) | 0.5923 |
| Quadratic κ (nested) | 0.7525 |
| Accuracy | 0.8241 |
| Severe errors (\|e\| ≥ 2) | 0.0634 |
| Within-1 accuracy | 0.9366 |
| Evaluation windows | 12,026 |

F1 falls 0.02 SD from the 5-seed estimate above; κ sits 1.3 SD below it. κ has
been the more volatile of the two across every run measured, so the gap is noted
rather than treated as drift.

**The blend weight is selected per export, not a fixed constant.** An earlier
export of this same notebook shipped `(0.20, 0.80)` at F1 0.5970 / κ 0.7811. The
grid's optimum is a broad plateau rather than a sharp peak — both pairs put ~80–85%
of the vote on the network, and 0.0047 F1 separates them, well inside the ±0.0129
seed SD. Neither pair is more canonical than the other. Read the weight from
`model_config.json`; `models/loader.py` refuses to run without it rather than
defaulting to a remembered value. Do not re-export in search of a particular pair —
selecting the run that scores best is the same bias that produced the withdrawn
0.715.

Selection bias (pooled − nested) measured at **+0.0100** (pooled F1 0.6023). The
per-fold weight distribution, previously unrecorded, is `w_xgb = 0.15` in 10 folds,
`0.30` in 4, `0.25` in 1 — the folds do *not* agree on one weight, which is why the
nested figure is the one quoted.

### Export reload verification

The saved artifacts reproduce the in-memory predictions they were exported from,
over the 200 windows in `artifacts/fixtures/parity_fixture.npz`:

| Artifact | max \|p_saved − p_memory\| | argmax agreement |
| --- | --- | --- |
| `mscgca_population.keras` | 5.36e-07 | 100.00% |
| `xgb_population.json` | 3.64e-12 | 100.00% |
| blended (0.15/0.85) | 4.77e-07 | 100.00% |

Float32 round-trip noise only. Asserted continuously by `tests/test_parity.py`.

### Artifact fingerprints (SHA-256, first 16 hex)

```
models/mscgca_population.keras    404f04d8d13f49bc
models/xgb_population.json        2a801f18dd6a4b47
scalers/feature_scaler.pkl        95dcfe74685280f9
config/model_config.json          b1775c4c6a7cf1cf
```

If a file on disk does not match, it is not the artifact these numbers describe.

The booster and scaler are bit-identical to the earlier export — same data, same
seed, deterministic fits. Only the network differs, which is ordinary GPU training
nondeterminism, and that is what moved the blend optimum and the metrics.

The scaler was pickled under **scikit-learn 1.6.1**; loading it under a different
minor version warns (`InconsistentVersionWarning`) and is not guaranteed. This is
what `requirements.txt` pins against.

### Reference: offline, non-causal

The non-causal 120-beat offline model reaches macro F1 = 0.682, κ = 0.855
(`notebooks/01_pipeline/notebook-improvements.ipynb` cell 14). The deployable
pipeline reaches 0.5923.

**The causal pipeline does not recover offline performance.** The ~0.090 F1 gap
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

### Wire format

`/stream` pushes and `/stress/latest` returns the same object. The stress
decision is nested under `stress`; the surrounding fields are the raw physiology
and provenance a consumer would otherwise have to re-derive from the beat stream.

```json
{
  "timestamp": 1787282898.4,
  "heartRate": 78.4,
  "rmssd": 34.1,
  "sdnn": 42.0,
  "stress": {
    "mode": "band",
    "level_low": 1,
    "level_high": 2,
    "label": "mild-to-moderate",
    "confidence": 0.10,
    "adjacent": true,
    "probabilities": {"relaxed": 0.08, "mild": 0.40, "moderate": 0.50, "high": 0.02},
    "continuous_score": 1.46
  },
  "signalQuality": 0.92,
  "windowStart": 1787282838.4,
  "windowEnd": 1787282898.4
}
```

In **point** mode, `stress.level` carries a single level and `level_low`/
`level_high` are absent; `adjacent` is `false`.

### Field definitions

| Field | Meaning |
| --- | --- |
| `timestamp` | POSIX seconds, float. Always equals `windowEnd`: labeling is endpoint, so the prediction describes the window's **last** beat (§2). |
| `heartRate` | `60000 / mean RR` of the window, bpm, one decimal. |
| `rmssd`, `sdnn` | Milliseconds, one decimal, from `hrv_features` indices 2 and 1. **Unscaled** — the post-`StandardScaler` vector is what the model consumes and is meaningless as physiology. |
| `stress.confidence` | The **margin** between the top two class probabilities, not the top probability. A band is emitted exactly when this falls below `CONFIDENCE_TAU` (0.15). |
| `stress.probabilities` | The blended 4-vector before argmax, keyed by class name, 3 decimals. Rounding means it may sum to 1 ± 0.002. |
| `stress.continuous_score` | Expected level under that distribution, `sum(i * p_i)` for `i` in 0..3, two decimals. In the example: `0(0.08) + 1(0.40) + 2(0.50) + 3(0.02) = 1.46`. **Derived convenience value, not a model output.** |
| `signalQuality` | Fraction of the window's beats that arrived usable, two decimals. |
| `windowStart` | `ts_buffer[0]`, POSIX float — the window's first beat. |
| `windowEnd` | `ts_buffer[-1]`, POSIX float. Equals `timestamp`. |

**`signalQuality` is heartbeat-data quality, not radio signal strength.** It
describes how clean and usable the continuous heartbeat/RR stream arriving from
the IoT watch was for this inference window. It is **not** Bluetooth/BLE link
strength, network signal, or battery level.

It is computed, never estimated: `clean_rr` already rejects beats outside
300–2000 ms and beats differing from their predecessor by more than 20%, then
interpolates over them. Its boolean mask is threaded through `/ingest` into
`StreamingInference.observe(..., ok=)` and averaged over the window buffer. So
92 usable beats out of 100 gives `0.92`. `1.0` means no artefacts were detected;
lower values mean a greater share of the window rests on reconstructed data, and
a consumer may reasonably discount the prediction accordingly.

### Authority

`stress.mode`, `stress.level`/`level_low`/`level_high` and `stress.label` are the
authoritative decision. `probabilities` and `continuous_score` are supplementary.
**Consumers must not re-derive a label by taking the argmax of `probabilities`,
or by rounding `continuous_score`** — either bypasses the confidence gate and
reintroduces the false precision the band exists to prevent.

**[UNVERIFIED]** The 80%-coverage tradeoff (F1 +0.053, severe errors −0.036) and
the 84.2% adjacent-error figure are midpoint-derived and uncited. Re-measure
before quoting.

## 7. Open Items

- **RESOLVED — Export verification output.** Recorded in §3: max
  `|p_saved − p_memory|` = 5.36e-07 (network), 3.64e-12 (booster), argmax
  agreement 100% on all 200 fixture windows.
- **RESOLVED — Per-fold blend weight distribution.** Recorded in §3: 10 folds
  select `w_xgb = 0.15`, 4 select `0.30`, 1 selects `0.25`. Selection bias is
  +0.0100, not the +0.0000 previously assumed.
- **`model_config.json` contents not recorded here.** The metrics, weights and
  feature order it carries are reproduced in §3 and asserted against
  `src/componentb/config.py` by `loader.check_config()`; the remaining fields are
  narrative. Paste the file if a fuller record is wanted.
- **Zero-shot sensor transfer** — WESAD (chest ECG) → Empatica (wrist PPG)
  collapses to F1 = 0.135, κ = −0.104. **[UNVERIFIED]**, and unrelated to the
  cross-dataset *mechanism* replication in the research paper, which succeeded.
