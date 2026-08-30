"""Model loading for the server.

Artifacts are committed, so the normal path is that they load. The
absent case is still handled — a partial checkout, or a working tree
mid-re-export — by accepting PPG and running beat detection while
refusing to predict. Failing at import time would take the whole
backend down over a missing file it can report instead.
"""

import logging

from componentb.inference.stream import StreamingInference
from componentb.models.loader import (
    check_config, load_ensemble_weights, load_model, load_scaler,
    load_xgb_model,
)

log = logging.getLogger(__name__)

_artifacts = None
_reason = None


def _load_once():
    """Load models and weights a single time, remembering any failure."""
    global _artifacts, _reason
    if _artifacts is not None or _reason is not None:
        return _artifacts

    try:
        # before anything is loaded: refuse if the export was trained
        # against different windowing/feature order than src/ assembles
        check_config()
        _artifacts = {
            "model": load_model(),
            "xgb_model": load_xgb_model(),
            "scaler": load_scaler(),
            "weights": load_ensemble_weights(),
        }
        w_xgb, w_cnn = _artifacts["weights"]
        log.info("models loaded; blend w_xgb=%.2f w_cnn=%.2f", w_xgb, w_cnn)
    except (FileNotFoundError, ImportError, KeyError, ValueError, OSError) as exc:
        _reason = str(exc)
        log.warning("running without a model: %s", _reason)
    return _artifacts


def unavailable_reason():
    _load_once()
    return _reason


def new_stream():
    """A per-connection inference engine, or None if artifacts are missing."""
    artifacts = _load_once()
    if artifacts is None:
        return None
    return StreamingInference(**artifacts)
