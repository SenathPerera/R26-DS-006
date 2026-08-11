"""Model loading for the server.

Artifacts are not in the repo (see .gitignore), so the server has to
cope with them being absent: it still accepts PPG and runs beat
detection, it just cannot predict. Failing loudly at import time would
make the whole backend un-runnable on a fresh clone.
"""

import logging

from componentb.inference.stream import StreamingInference
from componentb.models.loader import (
    load_ensemble_weights, load_ft_model, load_model, load_scaler,
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
        _artifacts = {
            "model": load_model(),
            "xgb_model": load_xgb_model(),
            "ft_model": load_ft_model(),      # optional, may be None
            "scaler": load_scaler(),
            "weights": load_ensemble_weights(),
        }
        log.info("models loaded; ft head: %s",
                 "yes" if _artifacts["ft_model"] is not None else "no")
    except (FileNotFoundError, KeyError, ValueError, OSError) as exc:
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
