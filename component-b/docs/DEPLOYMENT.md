# Deployment

## Local (demo / viva)

Run the backend on the laptop; all clients join over local WiFi.
No internet dependency — nothing to fail if venue WiFi is poor.

```bash
uvicorn server.main:app --host 0.0.0.0 --port 8000
```

Find the laptop IP:

```bash
# macOS / Linux
ipconfig getifaddr en0   ||  hostname -I
# Windows
ipconfig
```

Point clients at `ws://<that-ip>:8000/stream`.

## Endpoints

| Endpoint | Type | Consumer |
| --- | --- | --- |
| `/ingest` | WebSocket in | mobile app, raw PPG batches |
| `/stream` | WebSocket out | Quest (Component C), website — live push |
| `/stress/latest` | `GET` | anything that polls: curl, Postman, Unity |
| `/health` | `GET` | liveness |
| `/docs` | `GET` | auto-generated schema for the other components |

`/stress/latest` returns `503` until the first full window (~45 s at
`WINDOW_BEATS = 60`). Consumers must handle both `mode` values —
`"point"` carries `level`, `"band"` carries `level_low`/`level_high`.

```bash
curl http://<that-ip>:8000/stress/latest
```

Every prediction also carries `probabilities`, the blended 4-vector keyed
by class name, and `timestamp` as POSIX seconds (a float). `probabilities`
is supplementary: **do not re-derive a label from its argmax**, which
bypasses the confidence gate. See `ARCHITECTURE.md` §6.

## Required artifacts

Export these from `notebooks/05_deployment/notebook-train-export-2way.ipynb`
before the server will run. All are required — there is no partial mode:

```
artifacts/models/mscgca_population.keras     # p_cnn
artifacts/models/xgb_population.json         # p_xgb
artifacts/scalers/feature_scaler.pkl
artifacts/config/model_config.json           # incl. ensemble_weights
artifacts/fixtures/parity_fixture.npz        # tests only, not served
```

The whole `artifacts/` tree is gitignored. A file is identified by the
SHA-256 fingerprints in `ARCHITECTURE.md` §3 — check them before trusting
one.

The scaler must be the exact object fitted during training; one fitted on
different statistics normalises without error and silently moves every
prediction.

The shipped ensemble has **two** members, blended at `w_xgb = 0.20`,
`w_cnn = 0.80` read from `model_config.json`. There is no personalised
third head: it was evaluated and rejected (+0.0066 macro-F1, Wilcoxon
p = 0.625 — see `ARCHITECTURE.md` §3). If you find a
`mscgca_finetuned*.keras` anywhere, it is from the superseded pipeline and
nothing loads it.

If `model_config.json` is missing or has no `ensemble_weights {xgb, cnn}`,
the server starts but refuses to predict rather than guessing a blend —
`/stress/latest` keeps returning 503 and `/ingest` replies
`{"status": "model_unavailable"}` with the reason.

## Before trusting live output

Run the parity test. It checks two things, and both matter:

1. the loaded artifacts reproduce `artifacts/fixtures/parity_fixture.npz`,
   200 windows carrying the export notebook's own `p_xgb` / `p_cnn`
2. the streaming FIFO assembles the same features as the batch pipeline

```bash
pytest tests/test_parity.py -v
```

Without `artifacts/`, layer 1 **skips** — the run goes green while proving
nothing about the model. Check for `skipped` in the summary before
treating a pass as meaningful.
