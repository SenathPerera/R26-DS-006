"""Configuration constants.

These values are baked into the trained models. Changing any of them
requires retraining — they are not free parameters at inference time.
"""

# --- windowing ---
WINDOW_BEATS = 120          # what the shipped model was trained on
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

# --- output ---
CLASS_NAMES = ["relaxed", "mild", "moderate", "high"]
N_CLASSES = 4
CONFIDENCE_TAU = 0.15       # below this margin, emit a merged band
