"""Layer 2a tests: synthetic tones with KNOWN pitch must produce the
expected prosodic measurements."""

import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import SAMPLE_RATE
from componentd.layer2_prosody import FEATURE_NAMES, N_FEATURES, extract_prosody


def tone(freq_start: float, freq_end: float, seconds: float = 2.0) -> np.ndarray:
    """Tone sweeping from freq_start to freq_end Hz."""
    t = np.linspace(0, seconds, int(SAMPLE_RATE * seconds))
    freq = np.linspace(freq_start, freq_end, len(t))
    phase = 2 * np.pi * np.cumsum(freq) / SAMPLE_RATE
    return (0.3 * np.sin(phase)).astype(np.float32)


def test_output_shape_and_finite():
    vec = extract_prosody(tone(150, 150))
    assert vec.shape == (N_FEATURES,)
    assert np.all(np.isfinite(vec))


def test_rising_pitch_widens_f0_range():
    flat = extract_prosody(tone(150, 150))
    rising = extract_prosody(tone(120, 300))
    i = FEATURE_NAMES.index("f0_range")
    assert rising[i] > flat[i] + 20


def test_higher_pitch_detected():
    low = extract_prosody(tone(120, 120))
    high = extract_prosody(tone(280, 280))
    i = FEATURE_NAMES.index("f0_mean")
    assert high[i] > low[i] + 50


def test_silence_does_not_crash():
    vec = extract_prosody(np.zeros(SAMPLE_RATE * 2, dtype=np.float32))
    assert vec.shape == (N_FEATURES,)
    assert np.all(np.isfinite(vec))
