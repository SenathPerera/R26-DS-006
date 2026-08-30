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


def quiet(seconds=8.0, level=0.0025):
    """A genuinely quiet room: low broadband noise, no speech, long enough for
    the ambient step (~8s). ~-52 dBFS floor, comfortably under the gate."""
    rng = np.random.RandomState(0)
    return (level * rng.randn(int(SAMPLE_RATE * seconds))).astype(np.float32)


def loud_noise(seconds=8.0, level=0.06):
    """Loud background noise (fan/traffic), still no speech."""
    rng = np.random.RandomState(1)
    return (level * rng.randn(int(SAMPLE_RATE * seconds))).astype(np.float32)


def tonal_hum(seconds=8.0, freq=120.0, amp=0.011):
    """A steady low-frequency tone — a fan, AC, motor or building hum. Below the
    floor gate, tonal (low spectral flatness), energy concentrated <300 Hz."""
    t = np.linspace(0, seconds, int(SAMPLE_RATE * seconds), endpoint=False)
    return (amp * np.sin(2 * np.pi * freq * t)).astype(np.float32)


def broadband_noise(seconds=8.0, level=0.01):
    """Steady broadband noise (traffic, rain, crowd) at ~-40 dBFS — flat
    spectrum, above the floor gate."""
    rng = np.random.RandomState(2)
    return (level * rng.randn(int(SAMPLE_RATE * seconds))).astype(np.float32)


def quiet_with_transient(seconds=8.0):
    """A quiet room with one burst of clatter (a door, footsteps) ~0.5s long —
    the floor is fine but the peaks are not."""
    a = quiet(seconds)
    rng = np.random.RandomState(3)
    start = int(3.0 * SAMPLE_RATE)
    burst = (0.05 * rng.randn(int(0.5 * SAMPLE_RATE))).astype(np.float32)
    a[start:start + len(burst)] += burst
    return a


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
    # Loud fan/traffic noise (no speech) must also fail on the noise floor.
    r = check_ambient(loud_noise(), vad_fn=vad_none)
    assert not r["ok"]
    assert any("too_noisy" in x for x in r["reasons"])


def test_ambient_too_short_fails():
    r = check_ambient(quiet(0.3), vad_fn=vad_none)
    assert not r["ok"]
    assert any("too_short" in x for x in r["reasons"])


# ---- new: real acoustic analysis (WP1 / PROBLEM 1) -------------------
def test_ambient_names_a_tonal_hum():
    # A fan/AC hum in the usable band must NOT block the session (it raises the
    # floor but doesn't contaminate the voice, and is compensated at Layer 2) -
    # but it must still be NAMED a hum so the companion can mention the fan.
    r = check_ambient(tonal_hum(), vad_fn=vad_none)
    assert r["ok"], r["reasons"]
    assert r["noise_type"] == "hum", r["metrics"]
    assert r["verdict"] in ("good", "usable")


def test_ambient_names_broadband_noise():
    # Broadband noise (traffic/rain) in the usable band passes but is named, so
    # the companion can suggest closing a window rather than blocking outright.
    r = check_ambient(broadband_noise(), vad_fn=vad_none)
    assert r["ok"], r["reasons"]
    assert r["noise_type"] == "broadband", r["metrics"]


def test_ambient_transient_peaks_are_advisory():
    # A single burst of clatter (door, footsteps) must NOT block 30s of speech -
    # the peaks check is advisory (severity "warn"), and the room stays usable.
    r = check_ambient(quiet_with_transient(), vad_fn=vad_none)
    peaks = next(c for c in r["checks"] if c["id"] == "peaks")
    assert peaks["severity"] == "warn"
    assert not peaks["pass"]          # it did detect the transient
    assert r["ok"], r["reasons"]      # but a lone transient no longer blocks


def test_ambient_speech_is_one_check_among_several():
    # Speech detection still works, but is now one of several checks, each
    # exposed in the structured `checks` array.
    r = check_ambient(quiet(), vad_fn=vad_full)
    assert not r["ok"]
    assert any("voice_detected" in x for x in r["reasons"])
    assert r["noise_type"] == "voices"


def test_ambient_returns_structured_result():
    # The additive contract the mobile panel renders.
    r = check_ambient(quiet(), vad_fn=vad_none)
    assert r["ok"], r["reasons"]
    assert 0 <= r["score"] <= 100
    ids = {c["id"] for c in r["checks"]}
    assert ids == {"noise_floor", "peaks", "voices", "tonal_noise", "clipping", "duration"}
    for key in ["noise_floor_dbfs", "peak_dbfs", "dynamic_range_db",
                "spectral_flatness", "low_freq_ratio", "high_freq_ratio",
                "transient_count"]:
        assert key in r["metrics"], key
    # a good score must never override a failed check
    bad = check_ambient(loud_noise(), vad_fn=vad_none)
    assert not bad["ok"]


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
