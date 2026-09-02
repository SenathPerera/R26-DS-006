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


def _synth_probs(peaks: list[int]) -> dict:
    """A plausible 4-class distribution with mass on the peak level(s), so the
    fake mirrors B's `probabilities` field. D ignores it, but a faithful
    stand-in carries it."""
    base = 0.04
    p = {name: base for name in B_CLASS_NAMES}
    lead = (1.0 - base * len(B_CLASS_NAMES)) / len(peaks)
    for i in peaks:
        p[B_CLASS_NAMES[i]] = round(p[B_CLASS_NAMES[i]] + lead, 3)
    return p


def _continuous(probs: dict) -> float:
    """Expected level under the distribution, sum(i * p_i) - B's derived field."""
    return round(sum(i * probs[name] for i, name in enumerate(B_CLASS_NAMES)), 2)


def _physiology(rep_level: int) -> tuple[float, float, float]:
    """Synthetic HR/RMSSD/SDNN that move the right way with stress (higher
    stress -> lower HRV, higher HR). Fake numbers for demo, not measured."""
    rmssd = round(55.0 - 12.0 * rep_level, 1)   # 55, 43, 31, 19
    hr = round(66.0 + 6.0 * rep_level, 1)       # 66, 72, 78, 84
    sdnn = round(rmssd + 8.0, 1)
    return hr, rmssd, sdnn


def _stress_block() -> dict:
    """The gated decision, nested exactly like B's StressBlock."""
    common = lambda probs: {"confidence": _state["confidence"],
                            "probabilities": probs,
                            "continuous_score": _continuous(probs)}
    if _state["mode"] == "point":
        lvl = _state["level"]
        probs = _synth_probs([lvl])
        return {"mode": "point", "level": lvl, "label": B_CLASS_NAMES[lvl],
                "adjacent": False, **common(probs)}
    lo, hi = _state["level_low"], _state["level_high"]
    probs = _synth_probs([lo, hi])
    return {"mode": "band", "level_low": lo, "level_high": hi,
            "label": f"{B_CLASS_NAMES[lo]}-to-{B_CLASS_NAMES[hi]}",
            "adjacent": hi - lo == 1, **common(probs)}


@app.get("/stress/latest")
def latest(response: Response):
    if not _state["ready"]:
        # Exactly like real B before its first ~45s window.
        raise HTTPException(503, "no full window yet")
    now = time.time()
    block = _stress_block()
    # The representative level drives synthetic physiology (band -> the higher).
    rep = block["level"] if block["mode"] == "point" else block["level_high"]
    hr, rmssd, sdnn = _physiology(rep)
    # B's real envelope: the decision NESTED under "stress", physiology on top.
    return {"timestamp": now, "heartRate": hr, "rmssd": rmssd, "sdnn": sdnn,
            "stress": block, "signalQuality": 0.97,
            "windowStart": now - 60.0, "windowEnd": now}


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
