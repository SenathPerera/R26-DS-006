"""Central configuration for Component D.

Every module imports settings from here. Change a threshold or a model
in ONE place and the whole system follows.
"""

import os
from pathlib import Path

# ---------------------------------------------------------------- paths
# This file lives at <repo>/src/componentd/config.py, so the repo root is
# three parents up. Paths are anchored to the root, not the cwd, so the
# server/scripts/tests all resolve data + checkpoints identically.
ROOT = Path(__file__).resolve().parents[2]
DATA_DIR = ROOT / "data"                    # datasets (gitignored, never pushed)
MODELS_DIR = ROOT / "artifacts" / "models"  # trained checkpoints (gitignored)
DATA_DIR.mkdir(parents=True, exist_ok=True)
MODELS_DIR.mkdir(parents=True, exist_ok=True)

# ---------------------------------------------------------------- audio
# Every clip is resampled to this before touching any model.
SAMPLE_RATE = 16000
MIN_DURATION_SEC = 1.0          # shorter clips are rejected by Layer 1
MAX_DURATION_SEC = 35.0         # longer clips are trimmed

# --------------------------------------------- layer 1: quality gate
# Two DIFFERENT checks, not one reused function: the ambient step expects
# near-silence, the pre/post voice steps expect real speech. Using the
# same pass/fail logic for both was the root cause of a genuinely noisy
# room passing the ambient check - a same-clip "loud frames vs quiet
# frames" ratio measures the noise's own fluctuation when there is no
# speech to compare against, and real ambient noise (fans, traffic,
# chatter) is usually tonal/structured rather than flat white noise, so
# a flatness threshold tuned for hiss let it through undetected.
QUALITY = {
    # sanity checks shared by both (independent of VAD)
    "max_clip_ratio": 0.01,     # >1% samples at digital ceiling = distortion
    # ambient step: room must be genuinely quiet AND contain no speech
    "ambient_max_rms": 0.02,    # absolute noise floor ceiling
    "ambient_max_speech_sec": 0.3,   # tolerance for a brief cough/click
    # speech step: clip must contain enough actual voice to be scoreable
    "speech_min_rms": 0.005,    # below = silence / mic muted
    "speech_max_rms": 0.5,      # above = mic overload
    "speech_min_fraction": 0.25,     # >=25% of the clip must be VAD speech
}

# Ambient (Layer 1) — real acoustic analysis, replacing the single whole-clip
# mean-RMS check that never fired on device (an UNPROCESSED phone mic under-gains,
# so a noisy room and a quiet one both land near 0.003–0.012 whole-clip RMS; the
# steady FLOOR and the SPECTRAL shape are what actually separate them, not the mean).
# check_ambient() reads these; the old QUALITY["ambient_*"] keys stay for the
# web client / existing callers. UNCALIBRATED starting guesses — run
# scripts/calibrate_ambient.py on real Galaxy-A9 clips before trusting them.
AMBIENT = {
    # Steady background noise is graded in THREE states, not pass/fail: a fan or
    # AC (which raises the floor but doesn't contaminate the voice, and is
    # compensated for at Layer 2) must NOT block a session — only a genuinely
    # loud room does. Human speech + clipping stay hard fails (see check_ambient).
    "floor_good_max":       -45.0,  # at/below this the room reads as "quiet/good"
    "floor_too_noisy":      -30.0,  # ABOVE this we block; between = "usable"
    "floor_dbfs_max":       -42.0,  # legacy scoring target (web client / _ambient_score)
    "peak_dbfs_max":        -30.0,  # transient ceiling (now advisory, not a gate)
    "max_speech_sec":         0.3,  # nearby voices tolerance (unchanged)
    "max_clip_ratio":        0.01,  # mic overload (unchanged)
    "min_duration_sec":       6.0,  # we now listen ~8s; a short sample is rejected
    "low_freq_ratio_max":    0.70,  # tonal hum (fan/AC/motor) warning
    "high_freq_ratio_max":   0.40,  # electrical hiss / fluorescent buzz
    "dynamic_range_max_db":  26.0,  # wide spread = an unstable, event-filled room
    "transient_floor_mult":   6.0,  # a frame this far above the floor = a transient
}

# Silero VAD: a small (~1.8MB), fast, pretrained voice-activity detector.
# Used because arbitrary real-world noise (fans, traffic, background
# chatter, hums) cannot be reliably distinguished from speech by hand-
# tuned spectral thresholds - this is exactly the kind of narrow,
# well-solved sub-problem a frozen pretrained model is the right tool
# for, the same principle as Layer 2's frozen emotion encoder.
VAD_THRESHOLD = 0.5   # Silero's own speech-probability decision boundary

# --------------------------------------------- layer 2: stress model
# The frozen encoder. plus_large = 300M params trained on 42,500 hours
# of emotional speech; we use it ONLY as a feature extractor.
ENCODER_ID = "iic/emotion2vec_plus_large"

# Alternatives kept for the ablation table (same code path, one flag).
ENCODER_ABLATIONS = {
    "plus_large": "iic/emotion2vec_plus_large",       # 1024-d embedding
    "plus_base": "iic/emotion2vec_plus_base",         # 768-d, 90M params
    "base_finetuned": "iic/emotion2vec_base_finetuned",  # the PP1 encoder
}

# Architecture of the parts WE train (fusion gate + regression head).
FUSION = {
    "proj_dim": 128,            # both branches projected to this width
    "hidden_dim": 64,           # head hidden layer
    "dropout": 0.3,
}

TRAINING = {
    "batch_size": 64,
    "lr": 1e-3,
    "weight_decay": 1e-4,
    "max_epochs": 100,
    "early_stop_patience": 10,  # stop when validation CCC stops improving
}

# ------------------------------- emotion -> valence/arousal targets
# Public datasets label emotions, not stress. We map each emotion to
# coordinates on Russell's circumplex (valence = pleasant<->unpleasant,
# arousal = calm<->activated), both in [-1, 1]. The model learns to
# predict these two numbers; stress is derived from them below.
EMOTION_VA = {
    "neutral":   {"valence":  0.0, "arousal":  0.0},
    "joy":       {"valence":  0.7, "arousal":  0.5},
    "happiness": {"valence":  0.7, "arousal":  0.5},
    "surprise":  {"valence":  0.3, "arousal":  0.7},
    "anger":     {"valence": -0.6, "arousal":  0.8},
    "fear":      {"valence": -0.7, "arousal":  0.8},
    "sadness":   {"valence": -0.6, "arousal": -0.4},
    "disgust":   {"valence": -0.6, "arousal":  0.4},
    "calm":      {"valence":  0.4, "arousal": -0.5},
}

# Emotions with an unambiguous stressed/calm reading, used ONLY for
# binary evaluation metrics (surprise/disgust/sadness are excluded).
STRESSED_EMOTIONS = {"anger", "fear"}
CALM_EMOTIONS = {"neutral", "joy", "happiness", "calm"}

# Self-reported intensity scaling for collected data where the speaker
# graded their own take (e.g. "low stress" vs "high stress"). Scales the
# EMOTION_VA anchor toward neutral (0,0) rather than introducing separate
# magic numbers per level, so "high" still lands on the same point other
# datasets already use for that emotion (e.g. high stress ~= EMOTION_VA["fear"]).
INTENSITY_SCALE = {"low": 0.5, "mild": 0.75, "high": 1.0}


def scale_va_by_intensity(emotion: str, intensity: str) -> dict:
    """EMOTION_VA[emotion] scaled toward neutral by INTENSITY_SCALE[intensity]."""
    base = EMOTION_VA[emotion]
    scale = INTENSITY_SCALE[intensity]
    return {"valence": round(base["valence"] * scale, 4),
            "arousal": round(base["arousal"] * scale, 4)}


# Stress amplification when arousal is clearly POSITIVE (activated stress).
# 0.0 = pure valence-driven magnitude (the default we ship). Arousal never
# SUBTRACTS here: negative arousal on a stressed voice is the collapse artefact
# (Phase-0 evidence), not evidence of calm, so it must not pull the score down.
AROUSAL_GAIN = 0.0


def stress_from_va(valence: float, arousal: float,
                   arousal_gain: float = AROUSAL_GAIN) -> float:
    """Stress in [0, 1] from predicted valence/arousal — VALENCE-PRIMARY.

    Magnitude is driven by NEGATIVE VALENCE alone:  stress = max(0, -valence).
    A neutral or pleasant voice is, by Russell's circumplex, not in the stressed
    quadrant, so it maps to 0 (not to 0.5 as the old negativity form did — that
    flaw made calm real voices read as "mild" and barely separate from stressed).

    AROUSAL does NOT drive magnitude. The Phase-0 real-voice acid test proved
    arousal collapses on genuine (esp. quiet / "freeze") stress across all six
    checkpoints and BOTH languages, so it cannot be trusted here. Arousal is kept
    for two honest jobs: naming the stress TYPE (activated vs shutdown, below) and
    being the axis that cross-validates against Component B's HRV arousal.

    Evidence: valence-primary scoring raised stressed/calm separation (d') on
    every checkpoint and, with an LOO-calibrated threshold, lifted the active
    model from 75% (failing the Sinhala KPI) to >=88% on real voices. See
    docs/ABLATION_STUDY.md. The prior arousal-modifier form is kept as
    `stress_from_va_legacy` for the ablation comparison.
    """
    neg = max(0.0, -float(valence))                  # 0 at valence>=0, 1 at -1
    act = max(0.0, float(arousal))                   # only positive arousal adds
    return max(0.0, min(1.0, neg * (1.0 + arousal_gain * act)))


def stress_from_va_legacy(valence: float, arousal: float) -> float:
    """Superseded negativity-driven mapping (arousal as a [0.5,1] modifier).
    Retained ONLY for the PP2 ablation table; not used in the live pipeline."""
    arousal01 = (arousal + 1.0) / 2.0
    negativity01 = (1.0 - valence) / 2.0
    return negativity01 * (0.5 + 0.5 * arousal01)


def confidence_from_va(valence: float, arousal: float) -> float:
    """Confidence in [0, 1] = distance of VALENCE from neutral (|valence|).

    Valence is the trustworthy axis, so certainty tracks it: a voice whose
    valence sits near 0 is genuinely ambiguous -> low confidence, and Layer 4
    should lean on Component B there. This replaces the old sqrt(v^2+a^2), which
    was inflated by a confidently-wrong arousal value exactly on the collapse
    cases we most need to flag as uncertain."""
    return min(1.0, abs(float(valence)))


# --------------------------------------------- faint-recording guard
# A clip quieter than this RAW rms (measured before conditioning) is FAINT:
# loudness normalisation (conditioning.TARGET_RMS = 0.05) then amplifies it - and
# its noise floor - by TARGET_RMS/rms (5x at rms 0.01), which can turn a quiet,
# out-of-distribution voice into a CONFIDENT wrong reading. This is the exact
# Phase-3 failure mode: a ~0.01-rms calm clip scored 9.13/10 at confidence 0.91,
# sailing past Layer 4's 0.4 defer gate (see docs/ABLATION_STUDY.md Phase 3).
#
# The clip is still ABOVE the speech_min_rms reject floor, so we keep it - but we
# DOWN-WEIGHT its confidence in proportion to how faint it is. That single lever
# is load-bearing: Layer 3 widens its noise band when confidence drops, and Layer
# 4 defers to Component B below CONF_MIN - so a faint recording stops over-asserting
# on its own. A user-facing "faint_recording" warning also invites a re-record.
FAINT_INPUT_RMS = 0.02


def faint_confidence_penalty(raw_rms: float) -> float:
    """Multiplier in [0, 1] for voice confidence, given the clip's RAW (pre-
    conditioning) rms. 1.0 at/above FAINT_INPUT_RMS, then linear down to 0 as
    rms -> 0, so the confidence we report tracks how hard loudness-norm had to
    amplify the clip (and thus its noise). Pure + deterministic for testing."""
    r = float(raw_rms)
    if r >= FAINT_INPUT_RMS:
        return 1.0
    return max(0.0, r / FAINT_INPUT_RMS)


# Product-facing ordinal stress levels. Shared with Component B (HRV), whose
# WESAD model emits the SAME four levels - so Layer 4 compares like with like.
STRESS_LEVELS = ["no", "mild", "moderate", "high"]


# --------------------------------------------- Component B integration
# B streams HRV stress continuously and exposes its latest reading at
# GET {COMPONENT_B_URL}/stress/latest. Component D POLLS that endpoint at each
# episodic check-in (the ~30s pre and post speech moments) and feeds the result
# into Layer 4. B defaults to :8000 (D is :8010); override for a remote host.
COMPONENT_B_URL = os.environ.get("COMPONENT_B_URL", "http://127.0.0.1:8000")

# How long to wait on B before giving up and falling back to voice-only (seconds).
COMPONENT_B_TIMEOUT = float(os.environ.get("COMPONENT_B_TIMEOUT", "2.0"))

# B's StressPrediction carries an INTEGER level; these are its ordinal class names
# (componentb/config.CLASS_NAMES), index-aligned so B_CLASS_NAMES[level] names it.
# "relaxed" is later normalised to D's "no" - the two vocabularies otherwise match.
B_CLASS_NAMES = ["relaxed", "mild", "moderate", "high"]


# Binary stressed/not boundary on the 0-10 scale, LOO-calibrated on the real-voice
# set under valence-primary scoring (Youden's J, active model fusion_meld_baseline).
# "no" == not stressed; mild/moderate/high == stressed. See docs/ABLATION_STUDY.md.
STRESSED_THRESHOLD = 2.0


def stress_level_from_score(score_0_10: float) -> str:
    """0-10 stress score -> one of the four ordinal levels (matches Component B).

    The `no` boundary (STRESSED_THRESHOLD) is the LOO-VALIDATED binary stressed/
    calm cutoff. The mild/moderate/high sub-splits are PROVISIONAL: the real-voice
    set has only binary (stressed/calm) ground truth, not graded intensity, so the
    severity bands cannot yet be validated. They are recalibrated once graded
    labels or Component B's paired HRV are available (deferred, not guessed)."""
    if score_0_10 < STRESSED_THRESHOLD:      # 2.0 - validated boundary
        return "no"
    if score_0_10 < 4.5:                     # provisional
        return "mild"
    if score_0_10 < 7.0:                     # provisional
        return "moderate"
    return "high"


def stress_type_from_va(valence: float, arousal: float) -> str | None:
    """Only meaningful when stress is elevated: names WHICH kind of stress.
    'activated' = agitated (high arousal, e.g. anger/fear); 'shutdown' =
    withdrawn/exhausted (low arousal, the 'freeze' response). None when calm."""
    if stress_from_va(valence, arousal) < STRESSED_THRESHOLD / 10.0:  # not stressed
        return None
    return "activated" if arousal >= 0.0 else "shutdown"


# --------------------------------------------- layer 5: anomaly model
# The 12 numbers that summarise one complete session. Order matters:
# the autoencoder and every caller use exactly this order.
ANOMALY_FEATURES = [
    "pre_stress", "post_stress", "delta",
    "confidence_pre", "confidence_post",
    "session_duration", "hrv_agreement", "acoustic_variance",
    "ambient_rms", "session_number", "time_of_day", "days_since_last",
]
ANOMALY = {
    "hidden_dims": [16, 8, 4],  # encoder widths; decoder mirrors them
    "threshold_sigma": 3.0,     # anomaly if error > mean + 3*std
    "min_personal_sessions": 5, # switch to per-user threshold after this
}
