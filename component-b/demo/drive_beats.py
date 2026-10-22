"""Fallback demo: beats straight into the inference engine, no server.

Everything drive_ppg.py shows about *prediction generation* -- the
60-beat window, the 5-beat step, the confidence band -- but with no
WebSocket, no neurokit2 and no beat detection. If the network, the phone
or the PPG front end misbehaves in the room, this still runs.

    python drive_beats.py                 # the repo's committed RR fixture
    python drive_beats.py --source ramp   # longer synthetic ramp

It imports componentb from the component in place and reads the committed
artifacts; nothing is copied, so nothing can drift out of sync with src/.
"""

import argparse
import json
import sys
from pathlib import Path

# demo/ -> component-b/
REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "src"))

import numpy as np                                            # noqa: E402

from componentb.config import (                               # noqa: E402
    CLASS_NAMES, CONFIDENCE_TAU, STEP_BEATS, WINDOW_BEATS,
)
from componentb.inference.stream import StreamingInference    # noqa: E402

R, DIM, BOLD = "\033[0m", "\033[2m", "\033[1m"
GREEN, YELLOW, ORANGE, RED = (
    "\033[32m", "\033[33m", "\033[38;5;208m", "\033[31m")
LEVEL_COLOUR = {"relaxed": GREEN, "mild": YELLOW,
                "moderate": ORANGE, "high": RED}

RR_FIXTURE = REPO / "tests" / "fixtures" / "rr_segment.json"


def load_fixture():
    """The repo's own committed RR segment -- deterministic, no WESAD."""
    with open(RR_FIXTURE) as f:
        d = json.load(f)
    print(f"{DIM}{d['description']}{R}")
    t = d["t0"]
    for rr, temp in zip(d["rr_ms"], d["temp_c"]):
        yield float(rr), float(temp), t
        t += float(rr) / 1000.0


def ramp_beats(n, seed):
    """A longer stream that walks from relaxed to stressed."""
    from ppg_source import BeatGenerator
    gen = BeatGenerator("ramp", seed=seed, ramp_s=180.0)
    t = 1787000000.0
    for _ in range(n):
        rr = gen.next_rr_ms()
        yield rr, 33.6, t
        t += rr / 1000.0


def bar(p, width=18):
    return "#" * int(round(p * width)) + "." * (width - int(round(p * width)))


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--source", default="fixture",
                    choices=["fixture", "ramp"])
    ap.add_argument("--beats", type=int, default=400,
                    help="beats to generate for --source ramp")
    ap.add_argument("--seed", type=int, default=20260831)
    args = ap.parse_args()

    from componentb.models import loader
    cfg = loader.check_config()
    w_xgb, w_cnn = loader.load_ensemble_weights()

    print(f"{BOLD}Component B -- beat-driven inference{R}")
    print(f"  window {WINDOW_BEATS} beats, step {STEP_BEATS} beats, "
          f"tau {CONFIDENCE_TAU}")
    print(f"  blend  w_xgb={w_xgb} w_cnn={w_cnn}   "
          f"{DIM}(read from model_config.json, never hardcoded){R}")
    print(f"  LOSO   macro-F1 {cfg.get('loso_macro_f1')}  "
          f"kappa {cfg.get('loso_kappa')}")
    print(f"{DIM}  loading artifacts...{R}")

    engine = StreamingInference(
        model=loader.load_model(),
        xgb_model=loader.load_xgb_model(),
        scaler=loader.load_scaler(),
        weights=(w_xgb, w_cnn),
    )
    print(f"{DIM}  ready{R}\n")

    beats = (load_fixture() if args.source == "fixture"
             else ramp_beats(args.beats, args.seed))

    n = 0
    for i, (rr, temp, ts) in enumerate(beats, start=1):
        if not engine.observe(rr, temp, ts=ts, ok=True):
            if i <= WINDOW_BEATS and sys.stdout.isatty():
                print(f"\r{DIM}buffering {i}/{WINDOW_BEATS} beats{R}",
                      end="", flush=True)
            continue

        out = engine.predict()
        s = out["stress"]
        n += 1
        if n == 1:
            print(f"\r{DIM}window full at {WINDOW_BEATS} beats{R}")
        colour = (LEVEL_COLOUR.get(s["label"], "") if s["mode"] == "point"
                  else ORANGE)
        print(f"\n{DIM}#{n:<3} beat {i:<4} window "
              f"{out['windowEnd'] - out['windowStart']:.1f}s{R}")
        print(f"  {colour}{BOLD}{s['label'].upper():<22}{R} "
              f"{DIM}{'POINT' if s['mode'] == 'point' else 'BAND '}  "
              f"margin {s['confidence']:.3f}{R}")
        for name in CLASS_NAMES:
            p = s["probabilities"][name]
            print(f"    {LEVEL_COLOUR[name]}{name:<9}{R} "
                  f"{DIM}{bar(p)}{R} {p:5.3f}")
        print(f"  {DIM}HR {out['heartRate']:.1f} bpm   "
              f"RMSSD {out['rmssd']:.1f} ms   "
              f"SDNN {out['sdnn']:.1f} ms   "
              f"signalQuality {out['signalQuality']:.2f}{R}")

    print(f"\n{n} predictions from {i} beats "
          f"{DIM}(one per {STEP_BEATS} beats once the window filled){R}")


if __name__ == "__main__":
    main()
