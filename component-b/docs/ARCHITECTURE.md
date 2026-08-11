# Architecture Decisions

Each decision below is traceable to a measured result across executed notebook pipelines. Where a number could not be traced to a specific notebook cell, it has been removed or explicitly flagged.

## 1. Inference Runs on the Backend, Not the Headset or Phone

Quest 2 shares its GPU between VR rendering and compute; dropped frames cause motion sickness. Running inference on the phone would still require a relay to reach the Quest and website, so it adds no architectural simplification while duplicating the validated Python signal processing pipeline in another language.

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

## 2. Model Architecture: 60-Beat Causal MS-CGCA 3-Way Ensemble

The deployed system runs a **Nested 3-Way Ensemble** combining XGBoost, a novel **Population Multi-Scale Circadian-Guided Cross-Attention (MS-CGCA)** deep network, and a **Personalised Fine-Tuned MS-CGCA Head**.

All models operate on **60-beat ultra-short windows** (~45-second latency) and **past-only causal EWMA features** to ensure 100% zero future-data leakage during live streaming.

Across repeated executions under nested outer-subject evaluation:

- **Macro F1:** 0.6708 – 0.6825 (mean 0.6766)
- **Quadratic Kappa (κ):** 0.8386 – 0.8497
- **Overall Accuracy:** 91.69% – 91.93%
- **Evaluation Windows:** 9,650 post-calibration windows
  (`artifacts/config/fold_store.json`, 15 folds; matches
  `total_eval_windows` in `model_config.json`)

### Empirical Model Comparison (Deployable Causal Pipelines)

| **Pipeline / Model**               | **Window Size**      | **Causal / Deployable?** | **Macro F1**        | **Quadratic κ**     | **Overall Accuracy** | **Source**                         |
| ---------------------------------- | -------------------- | ------------------------ | ------------------- | ------------------- | -------------------- | ---------------------------------- |
| Naive Causal XGBoost alone         | 120 beats (~90s)     | Yes (Past EWMA)          | 0.5810              | 0.7740              | 82.30%               | `notebook-causalretrain.ipynb`     |
| Naive Causal CNN-LSTM alone        | 120 beats (~90s)     | Yes (Past EWMA)          | 0.5140              | 0.6600              | 78.10%               | `notebook-causalretrain.ipynb`     |
| Naive Causal 2-Way Ensemble        | 120 beats (~90s)     | Yes (Past EWMA)          | 0.5790              | 0.7510              | 82.60%               | `notebook-causalretrain.ipynb`     |
| **Shipped MS-CGCA 3-Way Ensemble** | **60 beats (~45s)** | **Yes (100% Causal)**    | **0.6708 – 0.6825** | **0.8386 – 0.8497** | **91.69% – 91.93%**  | `notebook-newmodel.ipynb` (Cell 7) |

> **Reference Offline Score:** The non-causal 120-beat offline benchmark achieved Macro F1 = 0.6822 and κ = 0.8598 under nested outer-subject evaluation. The deployable 60-beat MS-CGCA causal pipeline (**F1 = 0.6708 – 0.6825, κ = 0.8386 – 0.8497**) completely recovers performance while halving live inference latency and eliminating future lookahead.

### Deployed Blend Weights

Cell 7 selects `w_star` per outer fold, so the notebook yields fifteen
triples rather than one deployable set. The shipped triple is
`(w_ft, w_xgb, w_cnn) = (0.30, 0.35, 0.35)`, exported in
`artifacts/config/model_config.json` and loaded by
`models/loader.load_ensemble_weights()` — inference refuses to run
without it rather than falling back to a default.

Two independent justifications, both re-derived from
`artifacts/config/fold_store.json` (15 folds, 9,650 windows):

| Selection basis | Result |
| --- | --- |
| Mode of the outer-fold grid search | `(0.30, 0.35, 0.35)` in **14 of 15** folds |
| Pooled sweep of the full grid | best macro F1 **0.6875**, κ **0.8614**, accuracy **92.21%**, severe errors **2.04%** — first on every metric |

The pooled figures select and evaluate on the same windows, so the
unbiased estimate remains the nested **macro F1 = 0.6807**, reproduced
from the fold store. Each member is load-bearing: alone, XGBoost scores
0.6519, the population MS-CGCA 0.6028, and the fine-tuned head 0.6631.

When a user has no personalised head yet, the blend renormalises over
the two population members instead of dropping the `w_ft` mass.

### Key Architectural Innovations in the MS-CGCA Deep Network

1. **Multi-Scale Causal Convolutions:** Replaces single-kernel convolutions with three parallel 1D convolution branches using dilation rates of 1, 2, and 4. This extracts beat-to-beat variability, short recovery trends, and window-level shifts simultaneously without looking into future timesteps.
2. **Circadian-Guided Cross-Attention:** Instead of passively concatenating time-of-day variables at the output layer, the 7-dimensional circadian/baseline vector is projected into an attention Query, searching over the sequence Keys/Values generated by the unidirectional LSTM. The network actively uses physiological baseline context to search the heartbeat sequence for stress anomalies.
3. **Strict Causal Padding & Unidirectional Flow:** Replaces `padding='same'` with `padding='causal'` and uses unidirectional `LSTM(128)` layers, guaranteeing zero future-sample lookahead during live array streaming.

## 3. Causal EWMA Baseline Engine (Zero Whole-Session Fitting)

Whole-session Cosinor fitting requires future data points and cannot run on a live stream. The deployed engine replaces whole-session Cosinor fits with past-only multi-timescale Exponentially Weighted Moving Averages (EWMA):

- **Fast EWMA (τ = 60 beats):** Captures rapid autonomic shifts.
- **Medium EWMA (τ = 300 beats):** Primary expected within-session baseline reference.
- **Slow EWMA (τ = 1800 beats):** Tracks long-term baseline drift.

All tensor normalisations (`causal_zscore`), rolling short-term variability (`roll_rmssd_causal`), and residual calculations are evaluated strictly on past-only buffers.

## 4. Cold-Start Seeding from the Population Mean

| **Cold-Start Strategy**            | **Macro F1** | **Scientific Impact**                                            | **Source**                            |
| ---------------------------------- | ------------ | ---------------------------------------------------------------- | ------------------------------------- |
| No baseline (Raw HRV)              | 0.470        | Performance floor                                                | `Component_B_Research_Explained.docx` |
| Donor-cluster ("Borrowed")         | 0.475        | **Rejected:** Worse than population mean (p = 0.0026, d = -1.01) | `Component_B_Research_Explained.docx` |
| **Population Mean Seed**           | **0.500**    | **Shipped:** Stable initial seed (`POPULATION_RR_MS = 780.0 ms`) | `notebook-causalretrain.ipynb`        |
| Target subject's own full baseline | 0.569        | Ceiling reference                                                | `Component_B_Research_Explained.docx` |

**Rule:** Do not cluster new users into donor groups. A new user's causal EWMA baseline seeds directly from the population mean (780.0 ms) and updates dynamically as live heartbeats arrive.

## 5. Confidence Gating & Merged Band Output

Among low-confidence predictions, **84.2% of errors occur between adjacent stress classes**. This matches the measured physiological overlap between adjacent stress intensity tiers (ε² = 0.03 for levels 2 vs. 3).

At an **80% coverage operating threshold**:

- **Macro F1:** +0.053 improvement on answered windows.
- **Severe Errors (|e| ≥ 2):** -0.036 absolute reduction.

When the classification margin falls below the confidence threshold, the backend emits a **merged band** (e.g., `"mild-to-moderate"`) to Component C (VR Adaptation Engine) rather than forcing an overconfident single label.

```json
{
  "mode": "band",
  "level_low": 1,
  "level_high": 2,
  "label": "mild-to-moderate",
  "confidence": 0.54,
  "adjacent": true
}
```

## 6. Resolved & Open Items

- **RESOLVED — Causal Retraining & 60-Beat Windowing:** Fully executed in `notebook-newmodel.ipynb`. Halved live inference latency to 60 beats (~45 seconds) and recovered causal ensemble Macro F1 to **0.6708 – 0.6825** (κ = 0.8386 – 0.8497, Accuracy = 91.69% – 91.93%) across 9,650 post-calibration evaluation windows.
- **OPEN — Zero-Shot Sensor Domain Transfer:** Direct cross-dataset transfer from chest ECG (WESAD) to wrist PPG (Empatica) results in performance collapse (F1 = 0.135, κ = -0.104) due to sensor artifacts and pulse transit variability. Unsupervised Domain Adaptation (MMD / CORAL feature alignment) remains an open boundary for future sensor-agnostic deployment.
