"""A stand-in for Component B, for running D's B-integration locally WITHOUT the
wearable. It speaks Component B's EXACT wire contract (GET /stress/latest ->
StressPrediction; 503 until ready) so Component D's poll path is exercised over a
real HTTP round-trip. It does NOT compute HRV - it just serves whatever reading
you set, so you can drive a pre=stressed -> post=calm demo by hand.

This is a DEV/DEMO tool, not part of the product and not Component B's real code.
The genuine physiological run still needs B fed by a wearable or replayed PPG.

Run (separate terminal, from component-d/):
    .venv/bin/python scripts/fake_b_server.py            # serves :8000
Control it while D polls:
    curl -X POST 'http://127.0.0.1:8000/_set?level=moderate&confidence=0.82'
    curl -X POST 'http://127.0.0.1:8000/_set?level=relaxed&confidence=0.80'
    curl -X POST 'http://127.0.0.1:8000/_set?mode=band&low=mild&high=moderate'
    curl -X POST 'http://127.0.0.1:8000/_ready?ready=false'   # simulate 503
"""

import sys
import time
from pathlib import Path

import uvicorn
from fastapi import FastAPI, HTTPException, Response

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "src"))
from componentd.config import B_CLASS_NAMES   # ["relaxed","mild","moderate","high"]

app = FastAPI(title="Fake Component B")

# Mutable current reading. Starts NOT ready (503) to mirror B's warm-up.
_state = {"ready": False, "mode": "point", "level": 2,
          "level_low": 1, "level_high": 2, "confidence": 0.8}


def _idx(name: str) -> int:
    if name not in B_CLASS_NAMES:
        raise HTTPException(400, f"level must be one of {B_CLASS_NAMES}")
    return B_CLASS_NAMES.index(name)


@app.get("/stress/latest")
def latest(response: Response):
    if not _state["ready"]:
        # Exactly like real B before its first ~45s window.
        raise HTTPException(503, "no full window yet")
    now = time.time()
    if _state["mode"] == "point":
        lvl = _state["level"]
        return {"timestamp": now, "mode": "point", "level": lvl,
                "label": B_CLASS_NAMES[lvl], "confidence": _state["confidence"],
                "deviation": {"rmssd": -1.0, "sdnn": -0.8, "hr": 1.1},
                "baseline_maturity": "personal"}
    lo, hi = _state["level_low"], _state["level_high"]
    return {"timestamp": now, "mode": "band", "level_low": lo, "level_high": hi,
            "label": f"{B_CLASS_NAMES[lo]}-to-{B_CLASS_NAMES[hi]}",
            "confidence": _state["confidence"], "adjacent": hi - lo == 1,
            "deviation": {"rmssd": -0.3, "sdnn": -0.2, "hr": 0.4},
            "baseline_maturity": "converging"}


@app.post("/_set")
def set_reading(mode: str = "point", level: str = "moderate",
                low: str = "mild", high: str = "moderate",
                confidence: float = 0.8):
    """Dev control: set the reading D will get on its next poll."""
    if mode not in ("point", "band"):
        raise HTTPException(400, "mode must be 'point' or 'band'")
    _state.update(mode=mode, confidence=confidence, ready=True)
    if mode == "point":
        _state["level"] = _idx(level)
    else:
        _state["level_low"], _state["level_high"] = _idx(low), _idx(high)
    return {"ok": True, "state": _state}


@app.post("/_ready")
def set_ready(ready: bool = True):
    """Dev control: toggle the 503-not-ready state."""
    _state["ready"] = ready
    return {"ok": True, "ready": ready}


@app.get("/health")
def health():
    return {"ok": True, "ready": _state["ready"], "role": "fake-component-b"}


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description="Fake Component B (dev/demo stand-in)")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8000,
                    help="use another port if 8000 is taken; point D at it "
                         "with COMPONENT_B_URL=http://127.0.0.1:<port>")
    args = ap.parse_args()
    print(f"Fake Component B on http://{args.host}:{args.port} "
          "(starts 503; POST /_set to make it ready)")
    uvicorn.run(app, host=args.host, port=args.port, log_level="warning")
