"""Shared audio conditioning — the core of the preprocessing pipeline.

CRITICAL IDEA: this is the ONE place raw audio becomes model-ready
waveform. Both training feature-extraction (scripts/extract_features.py)
and live inference (src/layer2_inference.py) import from here, so a clip
is treated identically whether the model is LEARNING from it or SCORING
it. Any divergence between those two paths = train/serve skew = a model
that looks fine in training and flops in the live demo.

The four conditioning stages (full rationale in this folder's README):
  1a DC-offset removal  - centre the waveform on zero
  1b high-pass 70 Hz    - drop mic rumble / handling noise below speech
  1c silence trim       - remove dead air at start/end (keeps INTERNAL
                          pauses, which carry stress information)
  1d loudness normalise - put every clip at the same RMS so the model
                          cannot cheat by learning a dataset's recording
                          LEVEL instead of its STRESS (the Clever-Hans
                          confound when mixing studio + phone recordings)
"""

import sys
from pathlib import Path

import librosa
import numpy as np
from scipy.signal import butter, sosfilt

sys.path.insert(0, str(Path(__file__).parent.parent.parent))
from componentd.config import MAX_DURATION_SEC, MIN_DURATION_SEC, SAMPLE_RATE

# --- fixed parameters (decided; see README.md for why each value) ---
HIGHPASS_HZ = 70.0      # speech fundamentals sit above this; rumble below
HIGHPASS_ORDER = 2
TRIM_TOP_DB = 30.0      # trim start/end audio >30 dB below the clip's peak
TARGET_RMS = 0.05       # ~-26 dBFS; EVERY clip normalised to this loudness
SILENCE_RMS = 1e-4      # below this the clip is treated as silent
ANTI_CLIP_PEAK = 0.99   # rescale down if normalisation pushes past this


def _highpass(audio: np.ndarray, sr: int) -> np.ndarray:
    """2nd-order Butterworth high-pass at HIGHPASS_HZ (zero rumble, keeps
    the voice). sos form is numerically stable for low cutoffs."""
    sos = butter(HIGHPASS_ORDER, HIGHPASS_HZ / (sr / 2.0),
                 btype="highpass", output="sos")
    return sosfilt(sos, audio).astype(np.float32)


def condition(audio: np.ndarray, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Raw waveform -> conditioned 16 kHz waveform. Deterministic.
    Call this EXACTLY ONCE per clip (the high-pass is not idempotent)."""
    audio = np.asarray(audio, dtype=np.float32).flatten()
    if sr != SAMPLE_RATE:
        audio = librosa.resample(audio, orig_sr=sr, target_sr=SAMPLE_RATE)
    if audio.size == 0:
        return audio

    audio = audio - float(np.mean(audio))                 # 1a DC offset
    audio = _highpass(audio, SAMPLE_RATE)                 # 1b high-pass

    trimmed, _ = librosa.effects.trim(audio, top_db=TRIM_TOP_DB)  # 1c trim
    if trimmed.size >= int(0.2 * SAMPLE_RATE):   # keep trim only if speech left
        audio = trimmed

    rms = float(np.sqrt(np.mean(audio ** 2)))             # 1d loudness norm
    if rms > SILENCE_RMS:
        audio = audio * (TARGET_RMS / rms)
    peak = float(np.max(np.abs(audio))) if audio.size else 0.0
    if peak > ANTI_CLIP_PEAK:                             # anti-clip guard
        audio = audio * (ANTI_CLIP_PEAK / peak)

    return audio.astype(np.float32)


def prepare(audio: np.ndarray, sr: int = SAMPLE_RATE):
    """condition() + duration policy. Returns (audio, ok, reason).
    ok is False when nothing usable remains after trimming."""
    audio = condition(audio, sr)
    dur = audio.size / SAMPLE_RATE
    if dur < MIN_DURATION_SEC:
        return audio, False, f"too_short:{dur:.2f}s<{MIN_DURATION_SEC}s"
    audio = audio[: int(MAX_DURATION_SEC * SAMPLE_RATE)]  # cap max length
    return audio, True, "ok"


def preprocess_file(path: str):
    """Load any audio file (any format/rate) and run the full pipeline.
    Returns (audio, ok, reason)."""
    audio, sr = librosa.load(path, sr=SAMPLE_RATE, mono=True)
    return prepare(audio, SAMPLE_RATE)
