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

    `model` is the population MS-CGCA network, `xgb_model` the gradient
    booster, `ft_model` the optional personalised head. `weights` is the
    (w_ft, w_xgb, w_cnn) triple from `loader.load_ensemble_weights` —
    there is no default, because the notebook does not ship one.
    """

    def __init__(self, model=None, scaler=None, xgb_model=None,
                 ft_model=None, weights=None,
                 window=WINDOW_BEATS, step=STEP_BEATS):
        self.model = model
        self.scaler = scaler
        self.xgb_model = xgb_model
        self.ft_model = ft_model
        self.weights = weights
        self.window = window
        self.step = step
        self.rr_buffer = deque(maxlen=window)
        self.temp_buffer = deque(maxlen=window)
        self.ts_buffer = deque(maxlen=window)
        self.baseline = BaselineEngine()
        self.channels = CausalChannelState(window=window)
        self._since_last = 0

    def push(self, rr_ms, temp_c=None, ts=None):
        """Feed one beat. Returns a prediction dict, or None if the
        buffer is not yet full or the step interval has not elapsed."""
        self.rr_buffer.append(float(rr_ms))
        self.temp_buffer.append(float(temp_c) if temp_c is not None else np.nan)
        self.ts_buffer.append(float(ts) if ts is not None else time.time())
        self.baseline.update(rr_ms)
        self.channels.update(rr_ms, temp_c)
        self._since_last += 1

        if len(self.rr_buffer) < self.window:
            return None
        if self._since_last < self.step:
            return None

        self._since_last = 0
        return self._predict()

    def _window_timestamp(self):
        """Circadian features use the window MIDPOINT, not its edge.

        notebook-newmodel.ipynb cell 3: `mid = s + WINDOW//2`, and the
        circadian vectors are built from `tsk[bi]` at that index.
        """
        return self.ts_buffer[self.window // 2]

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
        """Blended class probabilities from the 3-way ensemble."""
        if self.model is None or self.xgb_model is None:
            raise RuntimeError(
                "no models loaded — export them from "
                "notebooks/05_deployment/notebook-newmodel.ipynb first"
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

        w_ft, w_xgb, w_cnn = self.weights
        if self.ft_model is None:
            # no personalised head yet (new user): renormalise over the
            # two population members rather than dropping mass
            scale = w_xgb + w_cnn
            return (w_xgb * p_xgb + w_cnn * p_cnn) / scale

        p_ft = np.asarray(self.ft_model.predict([seq, circ], verbose=0))[0]
        return w_xgb * p_xgb + w_cnn * p_cnn + w_ft * p_ft

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

        Justified by measurement: among low-confidence windows, 84.2%
        had the top two classes adjacent, matching the finding that
        neighbouring levels overlap physiologically.
        """
        probs = np.asarray(probs, dtype=float)
        order = np.argsort(probs)
        margin = float(probs[order[-1]] - probs[order[-2]])

        if margin >= tau:
            k = int(order[-1])
            return {
                "mode": "point",
                "level": k,
                "label": CLASS_NAMES[k],
                "confidence": round(margin, 3),
            }

        lo, hi = int(min(order[-2:])), int(max(order[-2:]))
        return {
            "mode": "band",
            "level_low": lo,
            "level_high": hi,
            "label": f"{CLASS_NAMES[lo]}-to-{CLASS_NAMES[hi]}",
            "confidence": round(margin, 3),
            "adjacent": bool(hi - lo == 1),
        }
