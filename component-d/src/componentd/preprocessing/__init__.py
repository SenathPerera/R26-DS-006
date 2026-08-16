"""Preprocessing package: raw audio -> clean, conditioned, model-ready waveform.

One shared entry point used by BOTH training and inference so a clip is
treated identically either way. See README.md for the panel-study notes.
"""

from .conditioning import (
    ANTI_CLIP_PEAK,
    HIGHPASS_HZ,
    SILENCE_RMS,
    TARGET_RMS,
    TRIM_TOP_DB,
    condition,
    prepare,
    preprocess_file,
)

__all__ = [
    "condition", "prepare", "preprocess_file",
    "HIGHPASS_HZ", "TRIM_TOP_DB", "TARGET_RMS", "SILENCE_RMS", "ANTI_CLIP_PEAK",
]
