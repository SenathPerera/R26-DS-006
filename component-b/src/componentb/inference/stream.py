"""Streaming inference.

Holds a rolling buffer and emits a prediction every STEP_BEATS.
Must produce identical output to the batch pipeline on the same
data — see tests/test_parity.py.
"""

from collections import deque

import numpy as np

from componentb.config import (
    WINDOW_BEATS, STEP_BEATS, CLASS_NAMES, CONFIDENCE_TAU,
)
from componentb.baseline.ewma import BaselineEngine


class StreamingInference:
    def __init__(self, model=None, scaler=None,
                 window=WINDOW_BEATS, step=STEP_BEATS):
        self.model = model
        self.scaler = scaler
        self.window = window
        self.step = step
        self.rr_buffer = deque(maxlen=window)
        self.temp_buffer = deque(maxlen=window)
        self.baseline = BaselineEngine()
        self._since_last = 0

    def push(self, rr_ms, temp_c=None):
        """Feed one beat. Returns a prediction dict, or None if the
        buffer is not yet full or the step interval has not elapsed."""
        self.rr_buffer.append(float(rr_ms))
        self.temp_buffer.append(float(temp_c) if temp_c is not None else np.nan)
        self.baseline.update(rr_ms)
        self._since_last += 1

        if len(self.rr_buffer) < self.window:
            return None
        if self._since_last < self.step:
            return None

        self._since_last = 0
        return self._predict()

    def _predict(self):
        rr = np.array(self.rr_buffer)
        # TODO: build the 7-channel sequence exactly as in training,
        # apply self.scaler, run self.model.
        raise NotImplementedError(
            "Wire this to the exported model — see notebooks/05_deployment"
        )

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
