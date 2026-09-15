"""Synthetic PPG generation for the demo driver.

Deliberately dependency-free (numpy only). The driver is a *data source*
standing in for the wearable; it must not import anything from
componentb, because the point of the demo is that beat detection, feature
extraction and inference all happen on the server side.

The waveform is built from an explicit RR-interval sequence, so the heart
rate and the beat-to-beat variability are things we set rather than
things we hope for. That is what makes the stress ramp reproducible: the
same seed and profile give the same beats every run.
"""

import numpy as np

# MAX30100 IR readings sit around 20-25k with a small AC component riding
# on a large DC offset. The exact scale is irrelevant -- nk.ppg_clean
# detrends and normalises -- but plausible numbers make the frames
# readable when you print one on screen.
DC_LEVEL = 22000.0
AC_AMPLITUDE = 900.0


class StressProfile:
    """Target physiology at a point in the session.

    `hr_bpm` sets the mean RR interval. `rsa_ms` is the amplitude of
    respiratory sinus arrhythmia, which is the dominant source of
    short-term HRV in a resting recording -- high when relaxed, strongly
    suppressed under sympathetic activation. Together they move mean_RR
    and RMSSD, which are the first two HRV features the model sees.
    """

    def __init__(self, hr_bpm, rsa_ms, jitter_ms=6.0, breath_hz=0.25):
        self.hr_bpm = float(hr_bpm)
        self.rsa_ms = float(rsa_ms)
        self.jitter_ms = float(jitter_ms)
        self.breath_hz = float(breath_hz)

    @staticmethod
    def blend(a, b, w):
        """Linear interpolation between two profiles, w in [0, 1]."""
        w = float(np.clip(w, 0.0, 1.0))
        return StressProfile(
            hr_bpm=a.hr_bpm + w * (b.hr_bpm - a.hr_bpm),
            rsa_ms=a.rsa_ms + w * (b.rsa_ms - a.rsa_ms),
            jitter_ms=a.jitter_ms + w * (b.jitter_ms - a.jitter_ms),
            breath_hz=a.breath_hz + w * (b.breath_hz - a.breath_hz),
        )


# Resting: slow heart, wide respiratory swing.
RELAXED = StressProfile(hr_bpm=62.0, rsa_ms=55.0, jitter_ms=7.0, breath_hz=0.22)
# Acute stress: faster heart, RSA largely gone, breathing shallower/faster.
STRESSED = StressProfile(hr_bpm=94.0, rsa_ms=9.0, jitter_ms=3.0, breath_hz=0.34)


def profile_at(name, elapsed_s, ramp_s=180.0):
    """The profile in force `elapsed_s` into the session."""
    if name == "calm":
        return RELAXED
    if name == "stress":
        return STRESSED
    if name == "ramp":
        # hold calm briefly so the first windows are unambiguous, then
        # climb. The first prediction lands ~48 s in, so the hold has to
        # outlast the warm-up or the panel never sees the low end.
        hold = 60.0
        return StressProfile.blend(
            RELAXED, STRESSED, (elapsed_s - hold) / ramp_s)
    raise ValueError(f"unknown profile {name!r}")


class BeatGenerator:
    """Emits RR intervals one at a time under a time-varying profile."""

    def __init__(self, profile_name, seed=20260831, ramp_s=180.0):
        self.profile_name = profile_name
        self.ramp_s = ramp_s
        self.rng = np.random.default_rng(seed)
        self.elapsed_s = 0.0
        self.beats = 0

    def next_rr_ms(self):
        p = profile_at(self.profile_name, self.elapsed_s, self.ramp_s)
        mean_rr = 60000.0 / p.hr_bpm
        # Respiratory sinus arrhythmia: a smooth oscillation in RR locked
        # to the breathing cycle. This is the component that collapses
        # under stress, so it carries most of the RMSSD signal.
        rsa = p.rsa_ms * np.sin(2 * np.pi * p.breath_hz * self.elapsed_s)
        rr = mean_rr + rsa + self.rng.normal(0.0, p.jitter_ms)
        # stay inside clean_rr's physiological gate (300-2000 ms) so the
        # demo does not silently manufacture artefacts
        rr = float(np.clip(rr, 340.0, 1900.0))
        self.elapsed_s += rr / 1000.0
        self.beats += 1
        return rr

    def current_profile(self):
        return profile_at(self.profile_name, self.elapsed_s, self.ramp_s)


def pulse_shape(phase):
    """One PPG beat over normalised phase [0, 1).

    Two Gaussians: the systolic upstroke, then the smaller dicrotic wave
    after the aortic valve closes. This is the standard two-component
    approximation and it gives ppg_peaks an unambiguous systolic maximum
    to find at 64 Hz.
    """
    systolic = np.exp(-((phase - 0.22) ** 2) / (2 * 0.075 ** 2))
    dicrotic = 0.32 * np.exp(-((phase - 0.52) ** 2) / (2 * 0.10 ** 2))
    return systolic + dicrotic


class PpgSynthesiser:
    """Turns a beat stream into a continuous 64 Hz waveform.

    Samples are produced strictly in time order and never regenerated, so
    frames can be cut from the stream at any boundary without a seam.
    """

    def __init__(self, generator, sample_rate=64.0, seed=20260831):
        self.gen = generator
        self.fs = float(sample_rate)
        self.rng = np.random.default_rng(seed + 1)
        self.t = 0.0                  # next sample time, seconds
        self.beat_start = 0.0
        self.beat_len = self.gen.next_rr_ms() / 1000.0

    def _advance_beat(self):
        self.beat_start += self.beat_len
        self.beat_len = self.gen.next_rr_ms() / 1000.0

    def take(self, n):
        """The next `n` samples of the waveform."""
        out = np.empty(n, dtype=float)
        for i in range(n):
            while self.t >= self.beat_start + self.beat_len:
                self._advance_beat()
            phase = (self.t - self.beat_start) / self.beat_len
            # slow baseline wander (~0.05 Hz) plus sensor noise, so
            # ppg_clean has something to actually do
            wander = 140.0 * np.sin(2 * np.pi * 0.05 * self.t)
            noise = self.rng.normal(0.0, 22.0)
            out[i] = (DC_LEVEL + wander
                      + AC_AMPLITUDE * pulse_shape(phase) + noise)
            self.t += 1.0 / self.fs
        return out
