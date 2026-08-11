# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Component B: real-time 4-level stress inference (relaxed / mild / moderate / high)
from wearable PPG, feeding an adaptive VR meditation system. Data flow:

```
Wearable (PPG + TMP117)
      | BLE
      v
  Mobile app          relays raw PPG, runs no model
      | WebSocket
      v
  Python backend      <- ALL inference happens here
      | WebSocket
   +--+--+
   v     v
 Quest  Website
```

Inference intentionally runs on the backend, not the headset or phone — the
Quest shares its GPU between rendering and compute, and putting inference on
the phone would still require a relay to reach the Quest/website, so it buys
no simplification while duplicating the pipeline in another language. See
`docs/ARCHITECTURE.md` for this and every other design decision, each traced
to a specific notebook cell/result.

## Commands

```bash
python -m venv .venv
source .venv/bin/activate        # Windows: .venv\Scripts\activate
pip install -r requirements.txt

# run the whole suite
pytest

# single file / single test
pytest tests/test_causality.py -v
pytest tests/test_parity.py::test_streaming_matches_batch -v

# run the backend (clients connect to ws://<laptop-ip>:8000/ingest and /stream)
uvicorn server.main:app --reload --host 0.0.0.0 --port 8000
```

`tests/test_parity.py` is the most important test in the repo — it asserts
streaming inference matches the batch/notebook pipeline exactly. Run it
before trusting any live output, and before involving real hardware.

## Design principles

- **`src/componentb/` is the single source of truth.** Both the research
  notebooks and the live server (`server/`) import from it — nothing is
  reimplemented in another language or file. This is what prevents
  training-serving skew, where live predictions silently drift from
  validated notebook results.
- **Every shipped decision must trace to a measured, reproducible result.**
  `docs/ARCHITECTURE.md` is the decision log: it cites the exact notebook
  and cell for each number, and explicitly documents figures that were
  removed because they couldn't be re-derived (e.g. a hardcoded "0.687" and
  an uncited "p = 0.5245" that appeared in an earlier draft). When adding a
  new decision, cite it the same way; when a number can't be traced, don't
  keep it "for reference" — remove it.
- **The baseline engine (`baseline/ewma.py`) must be strictly causal** —
  output at index i can only depend on samples up to i. This is verified in
  `tests/test_causality.py` by corrupting future samples and checking past
  output is unchanged. A Cosinor-fit baseline was measured and rejected for
  deployment for this reason (it needs the whole session); the deployed
  engine tracks three concurrent EWMA timescales instead.
- **New users seed from the population mean, not a donor cluster** —
  donor-cluster cold start was measured worse than the population mean
  (see `docs/ARCHITECTURE.md`). Don't reintroduce subject clustering for
  cold start without new evidence.
- **Low-confidence predictions emit a merged band, not a forced single
  label** (e.g. "mild-to-moderate"), gated by `CONFIDENCE_TAU` in
  `config.py`. This is a measured tradeoff (F1 +0.053, severe errors -0.036
  at 80% coverage), not a UX default — don't change the threshold without
  re-measuring.

## Architecture

`src/componentb/` — the validated pipeline, in dependency order:
- `config.py` — every constant that is baked into the trained model
  (window/step size, sample rates, artefact thresholds, EWMA halflives,
  class names, confidence tau). These are **not free parameters at
  inference time**; changing them requires retraining.
- `signal/ppg.py` — `ppg_to_rr` (beat detection via neurokit2) and
  `clean_rr` (artefact removal/interpolation). Ported directly from the
  validated notebook; not to be rewritten, since it's what produced the
  reported results.
- `features/hrv.py` — `hrv_features` (13 time/frequency-domain features)
  and `resid_features` (5 baseline-deviation features). **Feature order is
  load-bearing** — the scaler and model were fit against this exact order.
- `baseline/ewma.py` — `BaselineEngine`, the causal multi-timescale
  tracker described above.
- `models/loader.py` — loads `artifacts/models/*.keras`,
  `artifacts/scalers/*.pkl`, `artifacts/config/*.json`. The scaler loaded
  must be the exact object fit during training — normalizing with
  different statistics degrades predictions silently, with no error.
- `inference/stream.py` — `StreamingInference`: a rolling buffer that
  emits a prediction every `STEP_BEATS`, and `format_output`, which applies
  the confidence-gated point/band decision.

`server/` — FastAPI backend, the only place inference runs:
- `main.py` — two WebSocket endpoints: `/ingest` (mobile → backend, raw PPG
  batches) and `/stream` (backend → Quest/website, predictions), plus a
  `broadcast` helper for fan-out to subscribers.
- `schemas/messages.py` — the wire format (`PPGBatch` in, `StressPrediction`
  out) shared by both endpoints.

`clients/` — thin, model-free: `mobile/` relays raw PPG over BLE→WebSocket;
`unity/Scripts/StressClient.cs` and `web/` subscribe to `/stream` and render.

`notebooks/` — numbered by pipeline stage (`01_pipeline` → `05_deployment`);
results reported in `docs/ARCHITECTURE.md` are cited by notebook + cell.

`artifacts/` — model/scaler/config files, not committed (see `.gitignore`);
must be exported from the training notebook before the server can run.

## Current implementation state

Not everything described above is wired up yet — check before assuming:
- `server/main.py`'s `/ingest` handler is a stub (TODO: `ppg_to_rr` →
  `clean_rr` → `StreamingInference.push` → `broadcast`).
- `StreamingInference._predict` raises `NotImplementedError` — feature
  assembly into the model's expected input and the scaler/model call still
  need wiring, referenced as future work of `notebooks/05_deployment`
  (currently empty).
- `tests/test_parity.py::test_streaming_matches_batch` is skipped pending
  the model export.
- `BaselineEngine.maturity` thresholds are explicitly marked provisional in
  its docstring — the experiment that would validate them was invalidated
  and needs to be re-run with EWMA (see "Open items" in
  `docs/ARCHITECTURE.md`).

**Resolved:** `README.md` used to claim production shipped "the population
CNN alone", contradicting `docs/ARCHITECTURE.md`'s "two-way ensemble
(XGBoost + population CNN)"; `models/loader.py` also only loaded the CNN.
README now matches ARCHITECTURE.md, and `loader.py` gained
`load_xgb_model()` alongside `load_model()` so both ensemble members can be
loaded.
