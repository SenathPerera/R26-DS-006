"""Incremental construction of the 7-channel MS-CGCA sequence input.

`features/causal.py` holds the batch transforms exactly as the notebook
ran them. They cannot be applied to a 60-beat buffer directly: every one
of them carries state from the whole preceding session, so recomputing
over just the current window would produce different numbers from the
ones the model was trained on — training-serving skew that raises no
error and silently degrades predictions.

This module keeps that state per beat instead. Channel order, from
notebook-newmodel.ipynb cell 3:

    [rn, rm, sd, hr, rrn, tn, trn]

     rn   causal z-score of RR
     rm   rolling RMSSD of rn        (ROLL_WINDOW)
     sd   rolling SDNN of rn         (ROLL_WINDOW)
     hr   60000 / RR
     rrn  causal z-score of (RR - medium EWMA)
     tn   causal z-score of temperature
     trn  causal z-score of (temperature - its medium EWMA)

Order is load-bearing — the model was fit against this exact stacking.
"""

from collections import deque

import numpy as np

from componentb.config import (
    EWMA_HALFLIVES, POPULATION_RR_MS, ROLL_WINDOW, SEQ_CHANNELS,
    WINDOW_BEATS, ZSCORE_HALFLIFE,
)


def _alpha(halflife):
    return 1 - np.exp(np.log(0.5) / max(halflife, 1))


class _Ewma:
    """Streaming form of `ewma_causal`.

    Note the seed: the notebook seeds every EWMA with POPULATION_RR_MS,
    including the one it runs over temperature. That is arguably a quirk,
    but it is what the shipped model was trained against, so it is
    reproduced rather than corrected.
    """

    def __init__(self, halflife, seed=POPULATION_RR_MS):
        self.a = _alpha(halflife)
        self.state = float(seed)

    def update(self, x):
        self.state = self.a * x + (1 - self.a) * self.state
        return self.state


class _ZScore:
    """Streaming form of `causal_zscore`.

    The batch version seeds its mean with x[0], so the first output is
    exactly 0.0; the same seeding happens here on the first sample.
    """

    def __init__(self, halflife=ZSCORE_HALFLIFE):
        self.a = _alpha(halflife)
        self.m = None
        self.v = 1.0

    def update(self, x):
        if self.m is None:
            self.m = float(x)
        d = x - self.m
        self.m = self.m + self.a * d
        self.v = (1 - self.a) * (self.v + self.a * d * d)
        sd = np.sqrt(max(self.v, 1e-8))
        return (x - self.m) / (sd + 1e-8)


class CausalChannelState:
    """Feed beats in, read the model's sequence input out."""

    def __init__(self, window=WINDOW_BEATS, roll=ROLL_WINDOW):
        self.window = window
        self.roll = roll

        self._z_rr = _ZScore()
        self._z_res = _ZScore()
        self._z_temp = _ZScore()
        self._z_tres = _ZScore()
        self._ewma = {k: _Ewma(hl) for k, hl in EWMA_HALFLIVES.items()}
        self._temp_ewma = _Ewma(EWMA_HALFLIVES["medium"])

        self._rn_hist = deque(maxlen=roll)
        self._channels = [deque(maxlen=window) for _ in range(SEQ_CHANNELS)]
        # kept for the flat XGBoost vector, which uses the residual series
        # and the current fast/slow levels rather than the z-scored channels
        self._res_med = deque(maxlen=window)
        self._last_temp = None
        self.n_seen = 0

    def update(self, rr_ms, temp_c=None):
        """Feed one beat. Temperature carries forward if not resent."""
        rr = float(rr_ms)

        if temp_c is not None and not np.isnan(temp_c):
            self._last_temp = float(temp_c)
        if self._last_temp is None:
            raise ValueError(
                "no temperature seen yet — 2 of the 7 model channels come "
                "from the TMP117, so a prediction without it is not the "
                "validated pipeline"
            )
        temp = self._last_temp

        base = {k: e.update(rr) for k, e in self._ewma.items()}
        rn = self._z_rr.update(rr)
        self._rn_hist.append(rn)

        seg = np.array(self._rn_hist)
        rm = float(np.sqrt(np.mean(np.diff(seg) ** 2))) if len(seg) > 1 else 0.0
        sd = float(np.std(seg)) if len(seg) > 1 else 0.0

        hr = 60000.0 / (rr + 1e-8)
        res_med = rr - base["medium"]
        self._res_med.append(float(res_med))
        rrn = self._z_res.update(res_med)
        tn = self._z_temp.update(temp)
        trn = self._z_tres.update(temp - self._temp_ewma.update(temp))

        for buf, val in zip(self._channels, (rn, rm, sd, hr, rrn, tn, trn)):
            buf.append(float(val))
        self.n_seen += 1

    @property
    def ready(self):
        return len(self._channels[0]) >= self.window

    def sequence(self):
        """(window, 7) float32, or None until the buffer is full."""
        if not self.ready:
            return None
        return np.stack(
            [np.array(b, dtype=np.float32) for b in self._channels], axis=-1
        )

    def residual_window(self):
        """The (RR - medium EWMA) window feeding `resid_features`."""
        if not self.ready:
            return None
        return np.array(self._res_med, dtype=float)

    def expected(self):
        """Current EWMA level at each timescale (`base` in the notebook)."""
        return {k: e.state for k, e in self._ewma.items()}
