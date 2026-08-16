"""Tests for the shared audio conditioning pipeline.

Offline and deterministic (synthetic signals, no files, no models) so the
suite stays fast. These lock in the guarantees the rest of the system
relies on: every clip leaves preprocessing at the same loudness, centred,
resampled, and with silence trimmed.
"""

import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import SAMPLE_RATE
from componentd.preprocessing import TARGET_RMS, condition, prepare


def _tone(seconds=3.0, freq=200.0, amp=0.1, sr=SAMPLE_RATE):
    """A steady voiced-like tone (has no silence, so nothing to trim)."""
    t = np.arange(int(seconds * sr)) / sr
    return (amp * np.sin(2 * np.pi * freq * t)).astype(np.float32)


def test_loudness_normalised_to_target():
    rms = float(np.sqrt(np.mean(condition(_tone(amp=0.3)) ** 2)))
    assert abs(rms - TARGET_RMS) < 1e-3


def test_loudness_confound_removed():
    # THE key property: clips at very different input levels come out equal.
    base = _tone(amp=0.1)
    loud = float(np.sqrt(np.mean(condition(base * 8.0) ** 2)))
    quiet = float(np.sqrt(np.mean(condition(base * 0.1) ** 2)))
    assert abs(loud - quiet) < 1e-3


def test_dc_offset_removed():
    a = condition(_tone(amp=0.1) + 0.5)  # inject a large DC offset
    assert abs(float(np.mean(a))) < 1e-2


def test_output_is_finite_float32():
    a = condition(_tone())
    assert a.dtype == np.float32
    assert np.all(np.isfinite(a))


def test_leading_trailing_silence_trimmed():
    silence = np.zeros(SAMPLE_RATE, dtype=np.float32)
    padded = np.concatenate([silence, _tone(2.0), silence])
    assert condition(padded).size < padded.size


def test_resamples_to_16k():
    sr8 = 8000
    t = np.arange(int(3.0 * sr8)) / sr8
    x = (0.1 * np.sin(2 * np.pi * 200 * t)).astype(np.float32)
    a = condition(x, sr=sr8)
    assert a.size > 2 * SAMPLE_RATE   # ~3 s at 16 kHz


def test_prepare_rejects_too_short():
    _, ok, reason = prepare(_tone(seconds=0.3))
    assert ok is False and "too_short" in reason


def test_prepare_accepts_normal_clip():
    _, ok, _ = prepare(_tone(3.0))
    assert ok is True


def test_prepare_caps_max_duration():
    a, ok, _ = prepare(_tone(seconds=60.0))   # over MAX_DURATION_SEC (35s)
    assert ok is True and a.size <= 35 * SAMPLE_RATE
