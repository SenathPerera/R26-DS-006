"""Configuration constants.

These values are baked into the trained models. Changing any of them
requires retraining — they are not free parameters at inference time.
"""

# --- windowing ---
# WINDOW/STEP as trained: notebooks/05_deployment/notebook-train-export-2way
# .ipynb cell 1 ("WINDOW = 60", "STEP = 5"). The 120-beat window belongs to
# the superseded pipeline in notebook-causalretrain.ipynb.
WINDOW_BEATS = 60           # ~45 s of beats at rest
STEP_BEATS = 5              # -> a prediction roughly every 4 s

# Each window is labeled at its LAST beat: `y = labels[e-1]`, and the
# time-of-day feature index follows the label (`bi = min(e-1, len(ts)-1)`).
# Midpoint labeling was measured to inflate macro-F1 by +0.071 to +0.084
# across every configuration tested (notebook-deployment-decision.ipynb)
# because the window's own input postdates the labeled moment. Not a free
# parameter — see docs/ARCHITECTURE.md §2.
LABELING = "endpoint"

# --- signal ---
PPG_SAMPLE_RATE = 64.0      # Hz, Empatica-compatible
TEMP_SAMPLE_RATE = 4.0      # Hz, TMP117
RR_MIN_MS = 300             # 200 bpm — below this is artefact
RR_MAX_MS = 2000            # 30 bpm  — above this is artefact
RR_JUMP_THRESHOLD = 0.20    # >20% beat-to-beat change is artefact

# --- baseline (causal, deployable) ---
# [UNVERIFIED] Single-scale causal tracking cost 0.06 F1 vs offline
# Cosinor; three scales recovered most of it (0.557 vs 0.569). 0.557 is
# untraceable and 0.569 is midpoint-derived — see docs/ARCHITECTURE.md §5.
# Re-measure before quoting either. The halflives themselves are verified
# (notebook-train-export-2way.ipynb cell 1).
EWMA_HALFLIVES = {"fast": 60, "medium": 300, "slow": 1800}

# Cold start: seed from the population mean, NOT a donor cluster.
# [UNVERIFIED] Donor matching scored 0.475 — worse than the population
# 0.500. Both figures cite Component_B_Research_Explained.docx, which is
# not in this repo, and were measured under midpoint labeling
# (docs/ARCHITECTURE.md §5). The rule stands; the numbers need re-measuring.
POPULATION_RR_MS = 780.0

# Rolling window for the causal short-term variability channels
# (roll_rmssd_causal / roll_sdnn_causal), and the halflife used by
# causal_zscore. Both from notebook-train-export-2way.ipynb cell 1.
ROLL_WINDOW = 20
ZSCORE_HALFLIFE = 300

# --- MS-CGCA model inputs ---
# From notebook-train-export-2way.ipynb cell 4: build_ms_cgca(window=WINDOW,
# nch=7, ncirc=7, ncls=4). The deep network takes two inputs, not one.
SEQ_CHANNELS = 7            # rn, rm, sd, hr, rrn, tn, trn — order matters
CIRCADIAN_DIM = 7           # circ7(ts)
# XGBoost sees a flat vector, assembled in cell 3 as:
# hrv_features (13) + resid_features (5) + [base_fast, base_slow] (2)
# + circ_features (5)
XGB_FEATURE_DIM = 25

# Feature order is LOAD-BEARING: the scaler and the booster were fit
# against exactly these positions, and a permutation raises no error — it
# just moves every prediction. Declared in notebook-train-export-2way.ipynb
# cell 1, which asserts the built matrix matches; `loader.check_config`
# asserts the exported config still agrees with what is written here.
XGB_FEATURE_ORDER = [
    "mean_RR", "SDNN", "RMSSD", "pNN50", "CV_RR", "VLF", "LF", "HF",
    "LF/HF", "LF_nu", "SD1", "SD2", "SD1/SD2",
    "res_mean", "res_SD", "res_maxabs", "res_slope", "res_msq",
    "ewma_fast_level", "ewma_slow_level",
    "sin_24h", "cos_24h", "sin_90m", "cos_90m", "cortisol",
]

CNN_SEQUENCE_CHANNELS = ["rn", "rm", "sd", "hr", "rrn", "tn", "trn"]

# --- output ---
CLASS_NAMES = ["relaxed", "mild", "moderate", "high"]
N_CLASSES = 4
CONFIDENCE_TAU = 0.15       # below this margin, emit a merged band
