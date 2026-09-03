"""Replay the 200 real WESAD windows in the committed parity fixture.

drive_ppg.py proves the *transport and timing* of the live system, but it
feeds a synthetic waveform, and the model -- correctly -- calls a resting
synthetic signal `relaxed` throughout. It is out of distribution, and no
amount of raising the simulated heart rate changes that (measured: at
125 bpm with RMSSD 6 ms the model still says relaxed at p=0.45).

This script is the other half: real physiology with ground-truth labels,
so the model's discrimination is what gets demonstrated rather than the
plumbing. The windows come from artifacts/fixtures/parity_fixture.npz --
the notebook's own held-out feature windows, committed to the repo -- and
run through the shipped ensemble and the shipped confidence gate.

    python replay_fixture.py                 # terminal
    python replay_fixture.py --serve 8002    # feed the dashboard

Honesty note, say this out loud if you use --serve: these windows are
replayed, not streamed from a wearable. The model, the blend weights, the
confidence gate and the payload shape are the shipped ones; only the
transport is local. `signalQuality` is emitted as null rather than
invented, because the fixture stores finished feature windows and carries
no per-beat artefact mask to derive it from.
"""

import argparse
import asyncio
import json
import sys
import warnings
from pathlib import Path

warnings.filterwarnings("ignore")

# demo/ -> component-b/
REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "src"))

import numpy as np                                            # noqa: E402

from componentb.config import CLASS_NAMES                     # noqa: E402
from componentb.inference.stream import StreamingInference    # noqa: E402
from componentb.models import loader                          # noqa: E402

FIXTURE = REPO / "artifacts" / "fixtures" / "parity_fixture.npz"

R, DIM, BOLD = "\033[0m", "\033[2m", "\033[1m"
GREEN, YELLOW, ORANGE, RED = (
    "\033[32m", "\033[33m", "\033[38;5;208m", "\033[31m")
COLOUR = {"relaxed": GREEN, "mild": YELLOW,
          "moderate": ORANGE, "high": RED}


def build():
    """Run the shipped ensemble over every fixture window."""
    f = np.load(FIXTURE)
    Xx, Xs, Xc, y = f["X_xgb"], f["X_seq"], f["X_circ"], f["y"]

    scaler = loader.load_scaler()
    xgb = loader.load_xgb_model()
    model = loader.load_model()
    w_xgb, w_cnn = loader.load_ensemble_weights()

    p = (w_xgb * xgb.predict_proba(scaler.transform(Xx))
         + w_cnn * np.asarray(model.predict([Xs, Xc], verbose=0)))

    # X_xgb is the UNSCALED feature matrix, so columns 0/1/2 are
    # mean_RR, SDNN, RMSSD in their natural units
    # (config.XGB_FEATURE_ORDER).
    out = []
    t = 1787000000.0
    for i in range(len(p)):
        mean_rr, sdnn, rmssd = float(Xx[i, 0]), float(Xx[i, 1]), float(Xx[i, 2])
        span = mean_rr * 60 / 1000.0          # 60 beats at this mean RR
        out.append({
            "timestamp": round(t, 3),
            "heartRate": round(60000.0 / (mean_rr + 1e-8), 1),
            "rmssd": round(rmssd, 1),
            "sdnn": round(sdnn, 1),
            # the shipped gate, not a reimplementation of it
            "stress": StreamingInference.stress_block(p[i]),
            # NOT invented: the fixture holds finished feature windows and
            # no per-beat artefact mask, so there is nothing to derive
            # signalQuality from. CLAUDE.md forbids defaulting it.
            "signalQuality": None,
            "windowStart": round(t - span, 3),
            "windowEnd": round(t, 3),
            # replay-only extra, so the panel can see right vs wrong
            "trueLabel": CLASS_NAMES[int(y[i])],
        })
        t += 4.0                              # STEP_BEATS at ~75 bpm
    return out


def render(d, i, n_correct):
    s = d["stress"]
    colour = COLOUR.get(s["label"], "") if s["mode"] == "point" else ORANGE
    truth = d["trueLabel"]
    hit = truth in s["label"]
    mark = f"{GREEN}correct{R}" if hit else f"{RED}wrong{R}"
    print(f"\n{DIM}#{i:<4}{R} {colour}{BOLD}{s['label'].upper():<22}{R}"
          f" {DIM}{'POINT' if s['mode'] == 'point' else 'BAND '}"
          f"  margin {s['confidence']:.3f}{R}"
          f"   truth {COLOUR[truth]}{truth}{R}  {mark}"
          f"  {DIM}({n_correct}/{i} = {n_correct / i:.0%}){R}")
    bars = "  ".join(
        f"{COLOUR[c]}{c[:3]}{R} {s['probabilities'][c]:.2f}"
        for c in CLASS_NAMES)
    print(f"     {bars}   {DIM}HR {d['heartRate']:.0f} bpm  "
          f"RMSSD {d['rmssd']:.0f} ms{R}")


async def serve(rows, port, rate, start=0):
    """Publish the replay in StressPrediction shape for the dashboard."""
    import websockets

    clients = set()

    async def handler(ws, path=None):
        clients.add(ws)
        try:
            await ws.wait_closed()
        finally:
            clients.discard(ws)

    async def pump():
        i = start
        while True:
            row = rows[i % len(rows)]
            payload = json.dumps(row)
            for ws in list(clients):
                try:
                    await ws.send(payload)
                except Exception:
                    clients.discard(ws)
            i += 1
            if i % 25 == 0:
                print(f"{DIM}  replayed {i} windows "
                      f"to {len(clients)} client(s){R}")
            await asyncio.sleep(1.0 / rate)

    print(f"{BOLD}Replay server{R}  ws://127.0.0.1:{port}/stream")
    print(f"{DIM}  open  http://localhost:5500/?port={port}{R}")
    print(f"{DIM}  {len(rows)} real WESAD windows, looping "
          f"at {rate:g}/s{R}\n")
    async with websockets.serve(handler, "127.0.0.1", port):
        await pump()


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--serve", type=int, metavar="PORT",
                    help="publish on ws://127.0.0.1:PORT/stream "
                         "for web/index.html?port=PORT")
    ap.add_argument("--rate", type=float, default=1.0,
                    help="windows per second (default 1)")
    ap.add_argument("--limit", type=int, default=0,
                    help="terminal mode: stop after N windows")
    ap.add_argument("--start", type=int, default=0,
                    help="begin at this window index (0-based)")
    ap.add_argument("--bands-only", action="store_true",
                    help="replay ONLY the windows the gate emitted as a "
                         "band. Makes the confidence gate reachable on "
                         "demand instead of by luck -- say plainly that "
                         "you are filtering, it is 14%% of the set.")
    args = ap.parse_args()

    if not FIXTURE.exists():
        print(f"{RED}missing {FIXTURE}{R}")
        sys.exit(1)

    print(f"{DIM}loading artifacts and scoring 200 windows...{R}")
    rows = build()

    n = len(rows)
    bands = sum(r["stress"]["mode"] == "band" for r in rows)
    # two different numbers, and conflating them would overstate the result:
    #   strict  -- the top class alone is right (a band counts as a miss)
    #   covered -- the emitted answer contains the true class, which for a
    #              band means either of the two merged levels
    strict = sum(r["stress"]["mode"] == "point"
                 and r["stress"]["label"] == r["trueLabel"] for r in rows)
    covered = sum(r["trueLabel"] in r["stress"]["label"] for r in rows)
    print(f"{BOLD}{n} real WESAD windows{R}")
    print(f"  {strict}/{n} = {strict / n:.0%} correct as a point label")
    print(f"  {covered}/{n} = {covered / n:.0%} where the emitted answer "
          f"contains the true class {DIM}(bands count both levels){R}")
    print(f"  {bands}/{n} = {bands / n:.0%} emitted as a band")
    counts = {c: sum(r["stress"]["label"].startswith(c) for r in rows)
              for c in CLASS_NAMES}
    print(f"{DIM}predicted: {counts}{R}")

    if args.bands_only:
        rows = [r for r in rows if r["stress"]["mode"] == "band"]
        print(f"{ORANGE}--bands-only: replaying {len(rows)} band windows "
              f"only. This is a filtered view, not the whole set.{R}")

    if args.serve:
        try:
            asyncio.run(serve(rows, args.serve, args.rate, args.start))
        except KeyboardInterrupt:
            print("\nstopped")
        return

    import time
    n_correct = 0
    for i, row in enumerate(rows[args.start:], start=1):
        n_correct += row["trueLabel"] in row["stress"]["label"]
        render(row, i, n_correct)
        if args.limit and i >= args.limit:
            break
        time.sleep(1.0 / args.rate)


if __name__ == "__main__":
    main()
