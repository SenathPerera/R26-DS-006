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

## Required artifacts

Export these from `notebooks/05_deployment/notebook-newmodel.ipynb`
before the server will run:

```
artifacts/models/mscgca_population.keras     # p_cnn
artifacts/models/xgb_population.json         # p_xgb
artifacts/models/mscgca_finetuned.keras      # p_ft, optional (per-subject)
artifacts/scalers/feature_scaler.pkl
artifacts/config/model_config.json
```

The scaler must be the exact object fitted during training. The
fine-tuned head is optional: without it the blend falls back to the two
population members.

## Before trusting live output

Run the parity test. If streaming and batch predictions disagree on
the same input, there is a bug — find it before hardware is involved.

```bash
pytest tests/test_parity.py -v
```
