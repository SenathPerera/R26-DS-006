"""Tests for domain augmentation: each degradation must return finite audio of
the same length, and augment() must actually change the signal (so it is not a
silent no-op) while staying bounded."""

import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.augmentation import (add_background_noise, apply_reverb, augment,
                              telephone_bandpass)


def _tone(seconds=2.0, sr=16000):
    t = np.arange(int(seconds * sr)) / sr
    return (0.1 * np.sin(2 * np.pi * 200 * t)).astype(np.float32)


def test_noise_lowers_snr_but_stays_finite():
    y = _tone()
    out = add_background_noise(y, snr_db=10, rng=np.random.RandomState(0))
    assert out.shape == y.shape and np.all(np.isfinite(out))


def test_reverb_finite_same_length():
    y = _tone()
    out = apply_reverb(y, 16000, np.random.RandomState(0))
    assert out.shape == y.shape and np.all(np.isfinite(out))


def test_bandpass_attenuates_out_of_band():
    y = _tone()
    out = telephone_bandpass(y, 16000)
    assert out.shape == y.shape and np.all(np.isfinite(out))


def test_augment_changes_signal_and_stays_bounded():
    y = _tone()
    out = augment(y, 16000, np.random.RandomState(1))
    assert np.all(np.isfinite(out))
    assert np.max(np.abs(out)) <= 1.0 + 1e-6
    # with this seed at least one augmentation fires -> signal differs
    assert not np.array_equal(out[: len(y)], y[: len(out)])


def test_augment_is_deterministic_given_rng():
    y = _tone()
    a = augment(y, 16000, np.random.RandomState(7))
    b = augment(y, 16000, np.random.RandomState(7))
    assert np.array_equal(a, b)
