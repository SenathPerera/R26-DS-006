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
| `/session-update` | `POST` | Component B pushes its HRV stress level / band (push path) |
| `→ GET B:/stress/latest` | poll | D polls B at each check-in (`poll_b:true`); see §Component B |
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

Mapping (already aligned — see ARCHITECTURE §4): B's integer `level` →
`B_CLASS_NAMES[level]` → D's level (`relaxed→no`); B's band → higher level + low
confidence; B's `confidence` → D's Layer-4 `CONF_MIN` gate. If B returns 503 (no
reading yet / no wearable) D falls back to voice-only and reports "no heart data"
— integration degrades gracefully, never blocks a session.

**Implemented in** `src/componentd/component_b_client.py`: `map_stress_prediction`
(pure schema map), `poll_latest` (the HTTP call; 503/unreachable → `None`), and
`poll_into_store` (poll → feed the shared `StoredHRVProvider`, so the poll path and
B's push `/session-update` path produce identical Layer 4 input). Point D at B with
`COMPONENT_B_URL` (default `http://127.0.0.1:8000`).

Activate the poll **at `/infer`** with the query flag `poll_b=true`: D pulls B's
reading at that exact moment and stores it for the phase, so the pre body signal
is captured when the user speaks pre and the post signal when they speak post
(time-aligned - not both scraped at the end). `/full-session` then just compares
what is stored. Default `false` keeps the mock/push behaviour and every existing
test green. Fully unit-tested on D's side against B's real example payloads in
`tests/test_component_b_client.py` and at the endpoint in `tests/test_api.py`.

B only produces predictions while receiving live PPG into its `/ingest`; for a
demo without the wearable, B can replay recorded PPG (`component-b/data/replay/`)
to serve real `/stress/latest` values.

### Run the whole thing locally, no wearable (`fake_b_server.py`)

The wearable only gates B's real *physiology*; the D↔B **integration** can be run
end to end today against a stub that speaks B's exact contract. Three terminals,
from `component-d/`:

```bash
# T1 - stand-in for Component B (serves B's schema; starts 503 = not ready)
.venv/bin/python scripts/fake_b_server.py                 # :8000

# T2 - Component D (all five layers)
COMPONENT_B_URL=http://127.0.0.1:8000 .venv/bin/uvicorn server.main:app --port 8010

# T3 - web UI
cd clients/web && npm install && npm run dev              # :5173
```

Drive a pre-stressed → post-calm session by setting the stub between the two
recordings (this is what a real B would do on its own):

```bash
curl -X POST 'http://127.0.0.1:8000/_set?level=moderate&confidence=0.82'  # before PRE clip
# ... record PRE clip (D /infer with poll_b captures "moderate") ...
curl -X POST 'http://127.0.0.1:8000/_set?level=relaxed&confidence=0.80'   # before POST clip
# ... record POST clip (captures "no"); /full-session -> Layer 4 validates ...
```

> If `:8000` is taken on your machine, run the stub on another port and set
> `COMPONENT_B_URL` to match (e.g. `:8001`).

The UI's poll toggle passes `poll_b=true` to `/infer`. Without it (or with the
mock-HRV toggle) the pipeline still runs fully — B just isn't consulted.

### First live joint test (needs Senath's running B)

The stub proves the contract; the only thing left is B's real HRV. That step is a
smoke test, not a code change:

```bash
# 1. Senath starts REAL B (live wearable, or replayed PPG) on some host:
#    uvicorn server.main:app --host 0.0.0.0 --port 8000
# 2. From component-d/, point D at it and probe the live join:
COMPONENT_B_URL=http://<B-host>:8000 .venv/bin/python scripts/smoke_component_b.py
```

The script polls B's `/stress/latest`, prints the raw `StressPrediction` and D's
mapped `BodyReading`, and confirms a 503 degrades to voice-only. Green here = the
contract holds end-to-end against a real B.

## Before trusting live output

```bash
.venv/bin/python -m pytest tests/ -q          # 94 tests
.venv/bin/python scripts/evaluate_sinhala.py \
    --model artifacts/models/fusion_meld_baseline.pt \
    --metadata data/metadata_sinhala.csv       # honest Sinhala eval
```
