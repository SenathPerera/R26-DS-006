"""Configuration constants.

These values are baked into the trained models. Changing any of them
requires retraining — they are not free parameters at inference time.
"""

# --- windowing ---
# WINDOW/STEP as trained: notebooks/05_deployment/notebook-newmodel.ipynb
# cell 2 ("WINDOW = 60", "STEP = 5"). The 120-beat window belongs to the
# superseded pipeline in notebook-causalretrain.ipynb.
WINDOW_BEATS = 60           # ~45 s of beats at rest
STEP_BEATS = 5              # -> a prediction roughly every 4 s

# --- signal ---
PPG_SAMPLE_RATE = 64.0      # Hz, Empatica-compatible
TEMP_SAMPLE_RATE = 4.0      # Hz, TMP117
RR_MIN_MS = 300             # 200 bpm — below this is artefact
RR_MAX_MS = 2000            # 30 bpm  — above this is artefact
RR_JUMP_THRESHOLD = 0.20    # >20% beat-to-beat change is artefact

# --- baseline (causal, deployable) ---
# Single-scale causal tracking cost 0.06 F1 vs offline Cosinor;
# three scales recovered most of it (0.557 vs 0.569).
EWMA_HALFLIVES = {"fast": 60, "medium": 300, "slow": 1800}

# Cold start: seed from the population mean, NOT a donor cluster.
# Donor matching scored 0.475 — worse than the population 0.500.
POPULATION_RR_MS = 780.0

# --- MS-CGCA model inputs ---
# From notebook-newmodel.ipynb: build_novel_ms_cgca(window=WINDOW, nch=7,
# ncirc=7, ncls=4). The deep network takes two inputs, not one.
SEQ_CHANNELS = 7            # rn, rm, sd, hr, rrn, tn, trn — order matters
CIRCADIAN_DIM = 7           # circ7(ts)
# XGBoost sees a flat vector, assembled in the same cell as:
# hrv_features (13) + resid_features (5) + [base_fast, base_slow] (2)
# + circ_features (5)
XGB_FEATURE_DIM = 25

# --- output ---
CLASS_NAMES = ["relaxed", "mild", "moderate", "high"]
N_CLASSES = 4
CONFIDENCE_TAU = 0.15       # below this margin, emit a merged band
