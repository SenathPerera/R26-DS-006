"""Layer 1 tests: the two checks (ambient vs speech) must behave
differently and correctly. VAD is dependency-injected as a fast fake so
the suite stays offline and deterministic - the real Silero VAD is
exercised separately in scripts/check_layer1.py against real audio."""

import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import SAMPLE_RATE
from componentd.layer1_quality import check_ambient, check_speech


# --- fake VADs injected in place of Silero, so tests need no model ---
def vad_none(audio, sr):
    """Pretends there is no speech anywhere."""
    return []


def vad_full(audio, sr):
    """Pretends the entire clip is speech."""
    return [{"start": 0, "end": len(audio)}]


def vad_half(audio, sr):
    """Pretends the first half of the clip is speech."""
    return [{"start": 0, "end": len(audio) // 2}]


def quiet(seconds=3.0, level=0.008):
    """Low-level noise, no speech - a genuinely quiet room."""
    rng = np.random.RandomState(0)
    return (level * rng.randn(int(SAMPLE_RATE * seconds))).astype(np.float32)


def loud_noise(seconds=3.0, level=0.06):
    """Loud background noise (fan/traffic), still no speech."""
    rng = np.random.RandomState(1)
    return (level * rng.randn(int(SAMPLE_RATE * seconds))).astype(np.float32)


def speech_like(seconds=3.0):
    t = np.linspace(0, seconds, int(SAMPLE_RATE * seconds))
    carrier = 0.3 * np.sin(2 * np.pi * 150 * t) + 0.1 * np.sin(2 * np.pi * 450 * t)
    envelope = (np.sin(2 * np.pi * 3 * t) > 0).astype(float)
    return (carrier * envelope).astype(np.float32)


# ------------------------------- ambient check (expects silence) -----
def test_ambient_passes_quiet_room():
    r = check_ambient(quiet(), vad_fn=vad_none)
    assert r["ok"], r["reasons"]


def test_ambient_fails_when_voice_present():
    # The exact bug being fixed: a room that is not silent because
    # someone is talking must FAIL, even if levels are moderate.
    r = check_ambient(quiet(), vad_fn=vad_full)
    assert not r["ok"]
    assert any("voice_detected" in x for x in r["reasons"])


def test_ambient_fails_loud_room_even_without_speech():
    # Loud fan/traffic noise (no speech) must also fail on the rms floor.
    r = check_ambient(loud_noise(), vad_fn=vad_none)
    assert not r["ok"]
    assert any("too_noisy" in x for x in r["reasons"])


def test_ambient_too_short_fails():
    r = check_ambient(quiet(0.3), vad_fn=vad_none)
    assert not r["ok"]
    assert any("too_short" in x for x in r["reasons"])


# ------------------------------- speech check (expects a voice) ------
def test_speech_passes_with_enough_voice():
    r = check_speech(speech_like(), vad_fn=vad_full)
    assert r["ok"], r["reasons"]


def test_speech_fails_when_silent():
    # A near-silent clip has no voice to score - must fail.
    r = check_speech(quiet(), vad_fn=vad_none)
    assert not r["ok"]
    assert any("insufficient_speech" in x or "too_quiet" in x
               for x in r["reasons"])


def test_speech_fails_with_too_little_voice():
    # Loud enough, but VAD finds voice in only half -> below the 25%...
    # actually half is above 25%, so construct a clip where speech is a
    # small fraction: long clip, short speech segment.
    audio = speech_like(8.0)

    def vad_tiny(a, sr):
        return [{"start": 0, "end": int(0.5 * sr)}]  # 0.5s of 8s = 6%

    r = check_speech(audio, vad_fn=vad_tiny)
    assert not r["ok"]
    assert any("insufficient_speech" in x for x in r["reasons"])


def test_speech_clipping_fails():
    clipped = np.clip(speech_like() * 10, -1.0, 1.0)
    r = check_speech(clipped, vad_fn=vad_full)
    assert not r["ok"]
    assert any("clipping" in x for x in r["reasons"])


def test_metrics_always_reported():
    for check in (check_ambient, check_speech):
        r = check(speech_like(), vad_fn=vad_half)
        for key in ["duration_sec", "rms", "clip_ratio",
                    "speech_seconds", "speech_fraction", "speech_segments"]:
            assert key in r["metrics"]
