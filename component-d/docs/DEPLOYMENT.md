# Deployment — Component D

## Local (demo / viva)

Two processes: the Python API server and the web demo. Run both on the laptop.

```bash
# 1. backend  (repo root)
lsof -ti :8010 | xargs kill -9        # clear any stray server first
.venv/bin/uvicorn server.main:app --host 0.0.0.0 --port 8010

# 2. frontend (separate terminal)
cd clients/web
npm install        # first time only
npm run dev        # -> http://localhost:5173
```

Port **8010**, not 8000: 8000 clashes with common local dev servers, and on
macOS a `127.0.0.1` bind silently wins loopback over a `0.0.0.0` bind — a clash
there fails invisibly. Point the frontend at another host/port with
`VITE_API_BASE=http://<host>:<port> npm run dev`.

First `/infer` lazy-loads the ~1.8 GB encoder (~1–2 min); every call after is
fast.

## Endpoints

| Endpoint | Type | Consumer |
| --- | --- | --- |
| `/ambient-check` | `POST` | Layer 1 room-quality gate |
| `/infer` | `POST` | Layer 2 — one voice reading (pre/post) |
| `/session-update` | `POST` | Component B pushes its HRV stress level / band |
| `/full-session` | `POST` | Layers 3+4+5 combined report |
| `/companion/message` | `POST` | health-companion LLM turn |
| `/health` | `GET` | liveness + which layers are live |
| `/docs` | `GET` | auto-generated schema |

## Required artifacts

Checkpoints live in `artifacts/models/` (gitignored). The server disables any
endpoint whose checkpoint is missing with a clean 503 — it never crashes.

```
artifacts/models/fusion_meld_baseline.pt   # shipped stress model (default FUSION_CKPT)
artifacts/models/anomaly_v2.pt             # Layer 5 VAE
```

Override the stress checkpoint with `FUSION_CKPT=<stem> uvicorn ...` (stem of a
file in `artifacts/models/`).

## Component B integration (cross-modal, Layer 4)

Component B (`github.com/SenathPerera/R26-DS-006`, `component-b/`) runs its own
server and exposes `GET /stress/latest` returning a `StressPrediction`
(`mode` = `point`|`band`, `level`/`level_low`/`level_high`, `label`,
`confidence`). D is the session orchestrator and **polls B at each check-in**:

```text
before check-in : voice → L2 ; GET B:/stress/latest → map → store phase="pre"
after  check-in : voice → L2 ; GET B:/stress/latest → map → store phase="post"
report          : L4 compares (voice_pre,voice_post) × (body_pre,body_post)
```

Mapping (already aligned — see ARCHITECTURE §4): B's `label` → D's level
(`relaxed→no`); B's band → D's `band=[low,high]`; B's `confidence` → D's Layer-4
gate. If B returns 503 (no reading yet / no wearable) D falls back to voice-only
and reports "no heart data" — integration degrades gracefully, never blocks a
session.

B only produces predictions while receiving live PPG into its `/ingest`; for a
demo without the wearable, B can replay recorded PPG (`component-b/data/replay/`)
to serve real `/stress/latest` values.

## Before trusting live output

```bash
.venv/bin/python -m pytest tests/ -q          # 94 tests
.venv/bin/python scripts/evaluate_sinhala.py \
    --model artifacts/models/fusion_meld_baseline.pt \
    --metadata data/metadata_sinhala.csv       # honest Sinhala eval
```
