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
        self.baseline = BaselineEngine()
        self.channels = CausalChannelState(window=window)
        self._since_last = 0

    def observe(self, rr_ms, temp_c=None, ts=None):
        """Buffer one beat. Runs NO inference and needs no model.

        Beat arrival and inference have different cadences: beats land
        as the wearable sends them, predictions are due once per
        STEP_BEATS over a full window. Keeping them separate also lets
        a caller accumulate a calibration buffer during warmup, when
        there is deliberately nothing to predict with yet.

        Returns True when this beat completes a step boundary on a full
        window — i.e. when `predict()` is due.
        """
        self.rr_buffer.append(float(rr_ms))
        self.temp_buffer.append(float(temp_c) if temp_c is not None else np.nan)
        self.ts_buffer.append(float(ts) if ts is not None else time.time())
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
        probs = self._probabilities()
        out = self.format_output(probs)
        out["timestamp"] = self.ts_buffer[-1]
        out["deviation"] = {
            k: float(v) for k, v in
            self.baseline.residuals(self.rr_buffer).items()
        }
        out["baseline_maturity"] = self.baseline.maturity
        return out

    @staticmethod
    def format_output(probs, tau=CONFIDENCE_TAU):
        """Point estimate when confident, merged band when not.

        The blended distribution travels with the decision as
        `probabilities`, but `mode`, `level`/`level_low`/`level_high` and
        `label` are authoritative. A consumer that takes the argmax of
        `probabilities` bypasses the confidence gate and reintroduces the
        false precision the band exists to prevent.

        **[UNVERIFIED]** The supporting figure — 84.2% of low-confidence
        errors falling between adjacent classes — is midpoint-derived and
        uncited (docs/ARCHITECTURE.md §6). Re-measure before quoting it.
        """
        probs = np.asarray(probs, dtype=float)
        order = np.argsort(probs)
        margin = float(probs[order[-1]] - probs[order[-2]])
        distribution = {
            name: round(float(p), 4) for name, p in zip(CLASS_NAMES, probs)
        }

        if margin >= tau:
            k = int(order[-1])
            return {
                "mode": "point",
                "level": k,
                "label": CLASS_NAMES[k],
                "confidence": round(margin, 3),
                "probabilities": distribution,
            }

        lo, hi = int(min(order[-2:])), int(max(order[-2:]))
        return {
            "mode": "band",
            "level_low": lo,
            "level_high": hi,
            "label": f"{CLASS_NAMES[lo]}-to-{CLASS_NAMES[hi]}",
            "confidence": round(margin, 3),
            "adjacent": bool(hi - lo == 1),
            "probabilities": distribution,
        }
