# Component B — live demo harness

Shows two different things, and they must not be confused with each other:

1. **`drive_ppg.py`** — that the *system* works end to end. Raw PPG in
   over a WebSocket, beat detection and inference on the server,
   predictions out over a second WebSocket to two independent consumers.
2. **`replay_fixture.py`** — that the *model* discriminates. 200 real
   WESAD windows with ground-truth labels, all four classes, the
   confidence band.

Neither answers the other's question. A panel will notice if they are
conflated.

Nothing here is imported by `src/` or `server/`. It reads the component
in place — `sys.path` to `../src`, artifacts loaded through the normal
`loader` — so it cannot drift out of sync with what ships.

---

## Setup

```bash
cd component-b
python -m venv .venv
.venv/Scripts/activate          # Windows;  source .venv/bin/activate  elsewhere
pip install -r requirements.txt
```

The pin that matters is **scikit-learn 1.6.1**, the version
`feature_scaler.pkl` was fitted under. A different minor version
unpickles *without raising* and may transform differently — the silent
failure `models/loader.py` warns about. Do not run this on a system
Python that has 1.7.x.

All commands below are run from `component-b/`.

---

## Run the live path

Four terminals.

```bash
# 1  server  (8001 so the wearable can keep 8000 to itself)
PYTHONPATH=src .venv/Scripts/python -m uvicorn server.main:app \
    --host 127.0.0.1 --port 8001

# 2  dashboard
.venv/Scripts/python -m http.server 5599 -d demo/web
#    open http://localhost:5599/?port=8001

# 3  terminal consumer of the same socket
.venv/Scripts/python demo/watch_stream.py --port 8001

# 4  the driver
.venv/Scripts/python demo/drive_ppg.py --port 8001 --profile ramp
```

Port **5599**, not 5500 — 5500 is commonly taken by a Live Server
extension and the page then fails silently. Check with
`netstat -ano | grep :5599`.

The dashboard takes `?port=`, so one page serves every server:
`?port=8000` the wearable, `?port=8001` the synthetic driver,
`?port=8002` the fixture replay.

### Driver flags

```
--profile calm|stress|ramp   physiology to synthesise
--speed 4                    4x real time after warm-up
--warmup 6                   frames sent ungated to fill the first window
--frames 20                  stop after N
```

Warm-up compression is safe: the pipeline reads the frame's `timestamp`
field, never the wall clock, so `--speed` changes how long you wait and
nothing else. Without it there is a ~48 s silence while 60 beats
accumulate.

---

## Run the real-data path

```bash
.venv/Scripts/python demo/replay_fixture.py                       # terminal
.venv/Scripts/python demo/replay_fixture.py --serve 8002           # dashboard
.venv/Scripts/python demo/replay_fixture.py --bands-only --serve 8002 --rate .5
.venv/Scripts/python demo/replay_fixture.py --start 3              # the band window
```

`--bands-only` replays just the 27 windows the gate merged, so the
confidence band is reachable on demand rather than by luck — it is 14% of
the set, and at one window per second you could wait a minute. Say
plainly that you are filtering; the unfiltered summary stays printed
above it.

**Say this out loud when using `--serve`:** these windows are *replayed*,
not streamed from a wearable. The model, the blend weights, the
confidence gate and the payload shape are the shipped ones; only the
transport is local. `signalQuality` is emitted as `null`, never invented
— the fixture stores finished feature windows and carries no per-beat
artefact mask to derive it from.

---

## For whoever has the wearable

The hardware path does not involve this folder at all. The phone talks to
`/ingest` directly, exactly as `drive_ppg.py` does — same frame shape,
same status replies. What to check:

```bash
# server, reachable from the phone
PYTHONPATH=src .venv/Scripts/python -m uvicorn server.main:app \
    --host 0.0.0.0 --port 8000
```

- Phone on the same network; open the app's Wearable detail screen and
  set the endpoint to `ws://<laptop-LAN-IP>:8000/ingest`. Over USB
  instead: `adb reverse tcp:8000 tcp:8000` and keep the default.
- The laptop firewall must allow inbound TCP 8000.
- Watch it with `demo/watch_stream.py --port 8000` and
  `http://localhost:5599/?port=8000` — those work against the real
  wearable identically, because they only consume `/stream`.

**Expected behaviour:** frames every 15 s, `accepted` for each, and the
**first prediction after roughly 60 s** — four frames to reach 60 beats.
Then one every ~4 s. Silence before that is correct, not a fault.

Failure replies to expect on `/ingest`, all non-fatal:
`waiting_for_temperature` (no real TMP117 value yet — the server refuses
to infer without one), `invalid_batch` (frame is not exactly 960 finite
samples at 64.0 Hz), `processing_error` (beat detection failed on that
frame), `model_unavailable` (artifacts did not load).

Run `demo/drive_ppg.py --port 8000` first, with no wearable connected, to
confirm the server and the network path are good before blaming the
hardware. It sends the identical frame shape.

---

## Measured findings

Run, not assumed. Read before demonstrating.

**1. The shipped artifacts reproduce the training notebook.**
`pytest tests/test_parity.py` — 14 passed, 1 skipped (WESAD absent). The
artifact layer **ran**, it did not skip:
`test_scaler_and_xgb_reproduce_the_export`,
`test_mscgca_reproduces_the_export` and
`test_end_to_end_windows_match_the_export` all pass against the
notebook's own `p_xgb`/`p_cnn`. Strongest single piece of evidence in the
demo. Show it first.

**2. Beat detection works on the synthetic waveform.**
15-second frames yield 15 peaks at 62 bpm and 23 at 94 bpm, over the
10-peak floor in `ppg_to_rr`. Recovered HR matches target to 0.1 bpm;
RMSSD collapses 42 → 13 ms between calm and stress. `clean_rr` rejects
nothing, so `signalQuality` is 1.00.

**3. The synthetic driver does NOT change the predicted class.**
Measured across a sweep: `relaxed` wins at every combination tested, from
60 bpm / RMSSD 50 ms to **125 bpm / RMSSD 6 ms**, where it still holds
p=0.45. An abrupt step change does not move it either — confidence dips
to 0.91 for one window while the causal EWMA baseline catches up, then
recovers.

This is not a broken model; see finding 4. The synthetic waveform is out
of distribution, and the model correctly declines to call a resting
synthetic signal stressed. WESAD's stress classes were cut by a *local
RMSSD tertile within the stress condition*, so the discriminative signal
is deviation from the wearer's own recent baseline — and a causal EWMA
tracker absorbs any level held steady.

**Do not promise a panel that the level will climb when you raise the
simulated heart rate. It will not.**

**4. The fixture replay shows all four classes and the band.**
200 real windows. Three numbers, not one:

| | | |
|---|---:|---|
| raw argmax accuracy, gate ignored | **93%** | 186/200 — the model alone |
| correct as an emitted **point** label | **85%** | 170/200 — a band counts as a miss |
| emitted answer **contains** the true class | **97%** | 194/200 — for a band, either level |
| emitted as a band | **14%** | 27/200 |

Predicted counts: relaxed 143, mild 29, moderate 16, high 12. Window #4
is the one to point at — `MODERATE-TO-HIGH`, margin 0.143, ground truth
`high`: the gate refused to pick and the truth was inside the band.

Say which number you are quoting. The 97% is the weakest of the three,
because a band counts as correct if either level is right.

**Caveat on the on-screen tally:** the dashboard counts a running total
over however many windows have streamed so far, not the whole set. Early
in a replay it can read 99%; over a full 200-window loop it converges to
97%. Quote the table, not the screen.

---

## Suggested order

1. `pytest tests/test_parity.py -v` — the code reproduces the notebook
2. `demo/drive_ppg.py` + dashboard — raw PPG in, predictions out, live
3. `demo/replay_fixture.py` — real data, four classes, the band
4. `pytest tests/test_causality.py -v` — corrupt the future, past unchanged

---

## If something breaks

| Symptom | Cause | Do |
|---|---|---|
| `model_unavailable` | artifacts missing / import failed | `demo/drive_beats.py` — no server needed |
| driver prints `accepted 0` | server unreachable | check the port, then `/health` |
| no predictions after 60 s | fewer than 60 beats buffered | `--speed 0`, watch the frame counter |
| dashboard blank | opened as `file://`, or port taken | must be served over HTTP; try 5599 |
| `waiting_for_temperature` | first frame had `temperature: null` | driver always sends one; check the payload |
| dashboard stuck | server restarted | it reconnects itself every 1.5 s |

Everything degrades to `demo/drive_beats.py`, which needs no network, no
neurokit2 and no server.

---

## Known gap

The model trained on WESAD **chest ECG R-peaks at 700 Hz**; deployment
runs **wrist PPG at 64 Hz**. The pipeline consumes RR intervals rather
than the raw modality, and `clean_rr` plus `signalQuality` exist to
handle the noisier wrist source — but state this before a panel finds it.
