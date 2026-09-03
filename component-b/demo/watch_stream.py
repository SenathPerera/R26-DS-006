"""Subscribe to Component B's /stream and print each prediction.

The terminal counterpart of the browser dashboard. Run both at once:
two independent consumers on the same socket is the clearest statement
that the wire contract -- not any one UI -- is Component B's interface.

    python watch_stream.py --port 8001

Reads `stress.label` and `stress.mode` as authoritative and never takes
the argmax of `stress.probabilities`; doing so would bypass the
confidence gate that produces the merged band.
"""

import argparse
import asyncio
import json
import sys
from datetime import datetime

R = "\033[0m"
DIM, BOLD = "\033[2m", "\033[1m"
GREEN, YELLOW, ORANGE, RED, BLUE = (
    "\033[32m", "\033[33m", "\033[38;5;208m", "\033[31m", "\033[36m")

LEVEL_COLOUR = {"relaxed": GREEN, "mild": YELLOW,
                "moderate": ORANGE, "high": RED}
CLASSES = ["relaxed", "mild", "moderate", "high"]


def connect(url):
    try:
        from websockets.asyncio.client import connect as _c
    except ImportError:
        from websockets.client import connect as _c
    return _c(url, max_size=None)


def bar(p, width=18):
    filled = int(round(p * width))
    return "#" * filled + "." * (width - filled)


def render(d, n):
    s = d["stress"]
    label, mode = s["label"], s["mode"]
    colour = LEVEL_COLOUR.get(label, BLUE) if mode == "point" else ORANGE
    when = datetime.fromtimestamp(d["timestamp"]).strftime("%H:%M:%S")
    span = d["windowEnd"] - d["windowStart"]

    print()
    print(f"{DIM}#{n:<4} {when}  window {span:5.1f}s  "
          f"({d['windowStart']:.1f} -> {d['windowEnd']:.1f}){R}")
    tag = "POINT" if mode == "point" else "BAND "
    print(f"  {colour}{BOLD}{label.upper():<22}{R} "
          f"{DIM}{tag}  margin {s['confidence']:.3f}  "
          f"score {s['continuous_score']:.2f}{R}")

    if mode == "band":
        print(f"  {ORANGE}top two classes are within CONFIDENCE_TAU -- "
              f"the merged band IS the answer{R}")

    for name in CLASSES:
        p = s["probabilities"].get(name, 0.0)
        c = LEVEL_COLOUR.get(name, "")
        mark = "<" if name in label else " "
        print(f"    {c}{name:<9}{R} {DIM}{bar(p)}{R} {p:5.3f} {mark}")

    q = d["signalQuality"]
    qc = GREEN if q >= 0.95 else YELLOW if q >= 0.8 else RED
    print(f"  {DIM}HR {d['heartRate']:.1f} bpm   "
          f"RMSSD {d['rmssd']:.1f} ms   SDNN {d['sdnn']:.1f} ms   "
          f"signalQuality {R}{qc}{q:.2f}{R}")


async def run(args):
    url = f"ws://{args.host}:{args.port}/stream"
    print(f"{BOLD}Component B stream watcher{R}")
    print(f"  {url}")
    print(f"{DIM}  waiting -- the first prediction needs 60 buffered beats "
          f"(~48 s of signal){R}")
    async with connect(url) as ws:
        n = 0
        async for raw in ws:
            try:
                d = json.loads(raw)
            except ValueError:
                continue
            if "stress" not in d:
                continue
            n += 1
            render(d, n)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8001)
    args = ap.parse_args()
    # keep output live when piped to a file or a second pane
    try:
        sys.stdout.reconfigure(line_buffering=True)
    except AttributeError:
        pass
    try:
        asyncio.run(run(args))
    except KeyboardInterrupt:
        print("\nstopped")
    except OSError as exc:
        print(f"{RED}cannot reach the server: {exc}{R}")
        sys.exit(1)


if __name__ == "__main__":
    main()
