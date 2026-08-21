"""Streaming inference.

Holds a rolling buffer and emits a prediction every STEP_BEATS.
Must produce identical output to the batch pipeline on the same
data — see tests/test_parity.py.
"""

import time
from collections import deque

import numpy as np

from componentb.config import (
    WINDOW_BEATS, STEP_BEATS, CLASS_NAMES, CONFIDENCE_TAU,
    XGB_FEATURE_DIM,
)
from componentb.baseline.ewma import BaselineEngine
from componentb.features.channels import CausalChannelState
from componentb.features.circadian import circ7, circ_features
from componentb.features.hrv import hrv_features, resid_features


class StreamingInference:
    """Live counterpart of the notebook's windowing loop.

    `model` is the population MS-CGCA network and `xgb_model` the gradient
    booster — the two members of the shipped ensemble. `weights` is the
    `(w_xgb, w_cnn)` pair from `loader.load_ensemble_weights`; there is no
    default, because shipping a different blend than the one measured
    would change the output with no error.

    There is no third, personalised member. It was evaluated and rejected:
    +0.0066 macro-F1 at Wilcoxon p = 0.625, indistinguishable from seed
    noise, in exchange for per-user calibration state and a third artifact
    (docs/ARCHITECTURE.md §3).
    """

    def __init__(self, model=None, scaler=None, xgb_model=None,
                 weights=None,
                 window=WINDOW_BEATS, step=STEP_BEATS):
        self.model = model
        self.scaler = scaler
        self.xgb_model = xgb_model
        self.weights = weights
        self.window = window
        self.step = step
        self.rr_buffer = deque(maxlen=window)
        self.temp_buffer = deque(maxlen=window)
        self.ts_buffer = deque(maxlen=window)
        self.ok_buffer = deque(maxlen=window)
        self.baseline = BaselineEngine()
        self.channels = CausalChannelState(window=window)
        self._since_last = 0

    def observe(self, rr_ms, temp_c=None, ts=None, ok=True):
        """Buffer one beat. Runs NO inference and needs no model.

        Beat arrival and inference have different cadences: beats land
        as the wearable sends them, predictions are due once per
        STEP_BEATS over a full window. Keeping them separate also lets
        a caller accumulate a calibration buffer during warmup, when
        there is deliberately nothing to predict with yet.

        `ok` is this beat's entry from `clean_rr`'s mask: True if it
        arrived usable, False if it was rejected as an artefact and
        interpolated. It feeds `signal_quality` and nothing else — the
        model sees the repaired value either way.

        Returns True when this beat completes a step boundary on a full
        window — i.e. when `predict()` is due.
        """
        self.rr_buffer.append(float(rr_ms))
        self.temp_buffer.append(float(temp_c) if temp_c is not None else np.nan)
        self.ts_buffer.append(float(ts) if ts is not None else time.time())
        self.ok_buffer.append(bool(ok))
        self.baseline.update(rr_ms)
        self.channels.update(rr_ms, temp_c)
        self._since_last += 1

        if not self.at_step_boundary:
            return False
        self._since_last = 0
        return True

    @property
    def window_full(self):
        return len(self.rr_buffer) >= self.window

    @property
    def signal_quality(self):
        """Fraction of the window's beats that arrived usable.

        Quality of the incoming heartbeat/RR stream from the wearable —
        NOT BLE link strength, network signal or battery. 1.0 means
        `clean_rr` rejected nothing in this window; 0.92 means 8% of the
        beats were artefacts that had to be interpolated over, so the
        prediction rests partly on reconstructed data.
        """
        if not self.ok_buffer:
            return 0.0
        return round(sum(self.ok_buffer) / len(self.ok_buffer), 2)

    @property
    def at_step_boundary(self):
        return self.window_full and self._since_last >= self.step

    def predict(self):
        """Run the ensemble on the currently buffered window."""
        if not self.window_full:
            raise RuntimeError(
                f"window not full: {len(self.rr_buffer)}/{self.window} beats"
            )
        return self._predict()

    def push(self, rr_ms, temp_c=None, ts=None):
        """Buffer a beat and predict if it completes a step boundary.

        Convenience wrapper over `observe` + `predict` for callers that
        want one call per beat; the two are separable on purpose.
        """
        return self.predict() if self.observe(rr_ms, temp_c, ts) else None

    def _window_timestamp(self):
        """Circadian features are read at the window's LAST beat.

        notebook-train-export-2way.ipynb cell 3 (`build_endpoint`):

            li = e - 1                       # endpoint label
            bi = min(li, len(ts)-1)
            ... circ_features(ts[bi]) ... circ7(ts[bi])

        The time-of-day index follows the label, and the label sits at the
        window's end. The superseded 3-way pipeline read the midpoint
        (`notebook-newmodel.ipynb`); that scheme was measured to inflate
        macro-F1 by +0.071 to +0.084 across every configuration tested
        (`notebook-deployment-decision.ipynb`) and predicts a moment 30
        beats of its own input postdate. Do not restore it.
        """
        return self.ts_buffer[-1]

    def _xgb_vector(self):
        """The flat 25-dim vector, assembled in the notebook's order:
        hrv_features (13) + resid_features (5) + [fast, slow] (2)
        + circ_features (5)."""
        rr = np.array(self.rr_buffer)
        res_med = self.channels.residual_window()
        base = self.channels.expected()
        vec = np.concatenate([
            hrv_features(rr),
            resid_features(res_med),
            np.array([base["fast"], base["slow"]]),
            circ_features(self._window_timestamp()),
        ])
        if vec.shape[0] != XGB_FEATURE_DIM:
            raise ValueError(
                f"XGB vector is {vec.shape[0]}, expected {XGB_FEATURE_DIM} — "
                "feature order/count must match training exactly"
            )
        return vec

    def _probabilities(self):
        """Blended class probabilities from the 2-way ensemble."""
        if self.model is None or self.xgb_model is None:
            raise RuntimeError(
                "no models loaded — export them from "
                "notebooks/05_deployment/notebook-train-export-2way.ipynb "
                "first"
            )
        if self.weights is None:
            raise RuntimeError(
                "no ensemble weights — see loader.load_ensemble_weights"
            )

        seq = self.channels.sequence()[None, ...]
        circ = circ7(self._window_timestamp()).astype(np.float32)[None, ...]

        # the sequence input is already causally normalised; only the flat
        # vector goes through the scaler, as in the notebook
        flat = self._xgb_vector()[None, ...]
        if self.scaler is not None:
            flat = self.scaler.transform(flat)

        p_cnn = np.asarray(self.model.predict([seq, circ], verbose=0))[0]
        p_xgb = np.asarray(self.xgb_model.predict_proba(flat))[0]

        w_xgb, w_cnn = self.weights
        return w_xgb * p_xgb + w_cnn * p_cnn

    def _predict(self):
        return self.format_output(self._probabilities())

    def format_output(self, probs, tau=CONFIDENCE_TAU):
        """Assemble the full wire payload for the current window.

        Alongside the gated stress decision the payload carries the raw
        physiology a consumer would otherwise have to re-derive — heart
        rate, RMSSD and SDNN in their natural units, plus the window's
        span and how clean its input was.

        `timestamp` equals `windowEnd`: labeling is endpoint, so the
        prediction describes the window's last beat, not its middle.
        """
        # UNSCALED hrv_features: the scaler's output is what the model
        # consumes and is meaningless as physiology on the wire.
        # [mean_RR, SDNN, RMSSD, ...] — see config.XGB_FEATURE_ORDER.
        hrv = hrv_features(np.array(self.rr_buffer))
        mean_rr, sdnn, rmssd = float(hrv[0]), float(hrv[1]), float(hrv[2])

        return {
            "timestamp": self.ts_buffer[-1],
            "heartRate": round(60000.0 / (mean_rr + 1e-8), 1),
            "rmssd": round(rmssd, 1),
            "sdnn": round(sdnn, 1),
            "stress": self.stress_block(probs, tau),
            "signalQuality": self.signal_quality,
            "windowStart": self.ts_buffer[0],
            "windowEnd": self.ts_buffer[-1],
        }

    @staticmethod
    def stress_block(probs, tau=CONFIDENCE_TAU):
        """Point estimate when confident, merged band when not.

        The blended distribution travels with the decision as
        `probabilities`, but `mode`, `level`/`level_low`/`level_high` and
        `label` are authoritative. A consumer that takes the argmax of
        `probabilities` bypasses the confidence gate and reintroduces the
        false precision the band exists to prevent. `continuous_score` is
        likewise derived, not predicted.

        **[UNVERIFIED]** The supporting figure — 84.2% of low-confidence
        errors falling between adjacent classes — is midpoint-derived and
        uncited (docs/ARCHITECTURE.md §6). Re-measure before quoting it.
        """
        probs = np.asarray(probs, dtype=float)
        order = np.argsort(probs)
        margin = float(probs[order[-1]] - probs[order[-2]])

        common = {
            "confidence": round(margin, 3),
            "probabilities": {
                name: round(float(p), 3)
                for name, p in zip(CLASS_NAMES, probs)
            },
            # expected level under the distribution, sum(i * p_i)
            "continuous_score": round(
                float(np.dot(np.arange(len(probs)), probs)), 2),
        }

        if margin >= tau:
            k = int(order[-1])
            return {
                "mode": "point",
                "level": k,
                "label": CLASS_NAMES[k],
                "adjacent": False,
                **common,
            }

        lo, hi = int(min(order[-2:])), int(max(order[-2:]))
        return {
            "mode": "band",
            "level_low": lo,
            "level_high": hi,
            "label": f"{CLASS_NAMES[lo]}-to-{CLASS_NAMES[hi]}",
            "adjacent": bool(hi - lo == 1),
            **common,
        }
