"""Stand in for the wearable + phone: raw PPG into Component B's /ingest.

This is the client half of the live demo. It sends exactly what the
MindSync mobile app sends -- a 960-sample, 64 Hz frame with a temperature
-- and nothing else. No features, no RR intervals, no model output. Beat
detection, HRV extraction and inference all happen on the server, which
is the property the demo exists to show.

    python drive_ppg.py --port 8001 --profile ramp

The frame's `timestamp` field is what the pipeline reads for its circadian
features and window span; wall-clock send rate is irrelevant to the
result. That is why --speed can compress the ~48 s warm-up to a couple of
seconds without changing a single prediction.
"""

import argparse
import asyncio
import json
import sys
import time

from ppg_source import BeatGenerator, PpgSynthesiser

FRAME_SAMPLES = 960          # server.schemas.messages.COMPONENT_B_FRAME_SAMPLES
SAMPLE_RATE = 64.0           # server rejects anything else: Literal[64.0]
FRAME_SECONDS = FRAME_SAMPLES / SAMPLE_RATE      # 15.0

DIM, RESET, BOLD = "\033[2m", "\033[0m", "\033[1m"
GREEN, YELLOW, RED = "\033[32m", "\033[33m", "\033[31m"


def connect(url):
    try:
        from websockets.asyncio.client import connect as _c
    except ImportError:                       # websockets < 13
        from websockets.client import connect as _c
    return _c(url, max_size=None)


async def drain(ws, state, acked):
    """Print the server's replies as they arrive.

    /ingest answers with status frames only -- predictions go out on
    /stream to the dashboard and watch_stream.py. Seeing `accepted` here
    and a prediction there is the visible proof they are two different
    sockets.
    """
    try:
        async for raw in ws:
            try:
                msg = json.loads(raw)
            except ValueError:
                print(f"{DIM}  <- {raw}{RESET}")
                continue
            status = msg.get("status")
            if status == "accepted":
                state["accepted"] += 1
                acked.set()
            elif status == "waiting_for_temperature":
                print(f"{YELLOW}  <- waiting_for_temperature: "
                      f"{msg.get('detail')}{RESET}")
            elif status == "model_unavailable":
                state["fatal"] = msg.get("detail")
                acked.set()
                print(f"{RED}  <- model_unavailable: "
                      f"{msg.get('detail')}{RESET}")
            elif status == "invalid_batch":
                print(f"{RED}  <- invalid_batch: {msg.get('detail')}{RESET}")
            elif status == "processing_error":
                print(f"{RED}  <- processing_error: "
                      f"{msg.get('detail')}{RESET}")
            else:
                print(f"{DIM}  <- {raw}{RESET}")
    except Exception:
        pass


async def run(args):
    url = f"ws://{args.host}:{args.port}/ingest"
    gen = BeatGenerator(args.profile, seed=args.seed, ramp_s=args.ramp)
    ppg = PpgSynthesiser(gen, sample_rate=SAMPLE_RATE, seed=args.seed)

    # Frame timestamps are POSIX seconds and advance by exactly one frame
    # each time, independently of how fast we actually send.
    stamp = time.time() if args.start_now else 1787000000.0
    state = {"accepted": 0, "fatal": None}

    print(f"{BOLD}Component B demo driver{RESET}")
    print(f"  target      {url}")
    print(f"  profile     {args.profile}"
          + (f" (ramp over {args.ramp:.0f}s)" if args.profile == "ramp" else ""))
    print(f"  frame       {FRAME_SAMPLES} samples @ {SAMPLE_RATE:g} Hz "
          f"= {FRAME_SECONDS:g}s")
    print(f"  warm-up     {args.warmup} frames sent at full speed "
          f"(~{args.warmup * FRAME_SECONDS:.0f}s of signal)")
    print()

    async with connect(url) as ws:
        acked = asyncio.Event()
        reader = asyncio.create_task(drain(ws, state, acked))
        sent = 0

        async def await_ack(n):
            """Block until the server has acknowledged `n` frames.

            Beat detection and inference run per frame on the server, so
            an unthrottled warm-up would outrun it and the backlog would
            be lost when this client disconnects. Gating on the ack paces
            the sender to the server's real speed -- still far faster
            than real time, but nothing is dropped.
            """
            while state["accepted"] < n and not state["fatal"]:
                acked.clear()
                try:
                    await asyncio.wait_for(acked.wait(), timeout=30)
                except asyncio.TimeoutError:
                    print(f"{RED}  no ack for frame {n} after 30s{RESET}")
                    return

        try:
            while args.frames == 0 or sent < args.frames:
                samples = ppg.take(FRAME_SAMPLES)
                temp = 33.4 + 0.4 * (sent % 7) / 7.0   # plausible skin temp
                frame = {
                    "timestamp": round(stamp, 3),
                    "sample_rate": SAMPLE_RATE,
                    "ppg": [round(float(v), 3) for v in samples],
                    "temperature": round(temp, 2),
                }
                await ws.send(json.dumps(frame))
                sent += 1
                await await_ack(sent)

                p = gen.current_profile()
                elapsed = sent * FRAME_SECONDS
                print(f"{GREEN}->{RESET} frame {sent:>3}  "
                      f"t={stamp:.1f}  {FRAME_SAMPLES} samples  "
                      f"{DIM}session {elapsed:>5.0f}s | "
                      f"target {p.hr_bpm:.0f} bpm, RSA {p.rsa_ms:.0f} ms | "
                      f"beats {gen.beats:>4} | accepted {state['accepted']}"
                      f"{RESET}")

                if state["fatal"]:
                    print(f"\n{RED}Server has no model loaded. "
                          f"Nothing will be predicted.{RESET}")
                    print("Fall back to: python drive_beats.py")
                    break

                stamp += FRAME_SECONDS
                if sent > args.warmup and args.speed > 0:
                    await asyncio.sleep(FRAME_SECONDS / args.speed)
        except KeyboardInterrupt:
            pass
        finally:
            # `accepted` is sent before the frame is processed
            # (server/main.py sends it, then runs ppg_to_rr), so the last
            # frame may still be in flight. Closing here would discard it.
            await asyncio.sleep(args.linger)
            reader.cancel()

    print(f"\nsent {sent} frames, {state['accepted']} accepted")


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8001,
                    help="8001 = the synthetic demo server, 8000 = hardware")
    ap.add_argument("--profile", default="ramp",
                    choices=["ramp", "calm", "stress"])
    ap.add_argument("--ramp", type=float, default=180.0,
                    help="seconds to go from relaxed to stressed")
    ap.add_argument("--speed", type=float, default=1.0,
                    help="playback multiplier after warm-up; 0 = no delay")
    ap.add_argument("--warmup", type=int, default=4,
                    help="frames sent with no delay, to fill the first "
                         "60-beat window fast. Results are identical either "
                         "way -- the pipeline reads the timestamp field, "
                         "not the clock.")
    ap.add_argument("--frames", type=int, default=0,
                    help="stop after N frames (0 = run until Ctrl-C)")
    ap.add_argument("--seed", type=int, default=20260831)
    ap.add_argument("--linger", type=float, default=3.0,
                    help="seconds to stay connected after the last frame, "
                         "so the server can finish processing it")
    ap.add_argument("--start-now", action="store_true",
                    help="stamp frames with the real current time instead "
                         "of a fixed epoch (fixed is reproducible)")
    args = ap.parse_args()

    try:
        asyncio.run(run(args))
    except KeyboardInterrupt:
        print("\nstopped")
    except OSError as exc:
        print(f"{RED}cannot reach the server: {exc}{RESET}")
        print(f"Is it running?  uvicorn server.main:app "
              f"--port {args.port}   (from component-b/)")
        sys.exit(1)


if __name__ == "__main__":
    main()
