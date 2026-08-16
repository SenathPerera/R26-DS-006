# CogniVoice — Component D: Voice-Based Stress Detection

Multilingual voice stress scoring for the COGNIFY VR meditation system.
A health companion converses with the user before and after each session;
Component D turns those recordings into a stress level, validates it against
HRV, and flags anomalous sessions.

**Language status (honest):** the pipeline is language-agnostic by design, but
results are not uniform. English is validated and works. Sinhala is evaluated
zero-shot and is **limited** — the frozen emotion2vec encoder is
out-of-distribution for it (see `docs/ABLATION_STUDY.md`, Phase 2). Tamil has
dataset support (`src/datasets/emota.py`) but is **not yet evaluated** — future
work. The honest fix for weak-language voices is the multimodal design: when the
voice reading is low-confidence, Layer 4 defers to Component B's HRV.

## The five layers

| Layer | File | Technique | Trained by us |
|---|---|---|---|
| 1. Quality gate | `src/componentd/layer1_quality.py` | DSP rules (RMS, clipping, SNR) | - |
| 2. Stress scoring | `src/componentd/layer2_*.py` | frozen emotion2vec+ prosody branch + **gated fusion + V/A head** | **yes** |
| 3. Pre/post compare | `src/componentd/layer3_compare.py` | confidence-weighted statistics | - |
| 4. Cross-modal | `src/componentd/layer4_crossmodal.py` | voice vs HRV rules, 4 mismatch types | - |
| 5. Anomaly detection | `src/componentd/layer5_anomaly.py` | per-user autoencoder (VAE) | **yes** |

## Project layout

Mirrors the shared-repo convention (matches `component-b/`): `src/componentd/`
is the single source of truth; the server, scripts, tests and notebooks all
import from it.

```text
src/componentd/     the validated pipeline (config + 5 layers + preprocessing/datasets/companion)
server/main.py      FastAPI app — the only entry point clients talk to
clients/web/        panel-demo web UI (React/Vite)
artifacts/models/   trained checkpoints (gitignored)
scripts/            training / evaluation entry points
tests/              94 tests
docs/               ARCHITECTURE.md, DEPLOYMENT.md, ABLATION_STUDY.md
notebooks/          Colab training / analysis
data/               datasets (gitignored)
```

## Quick start

```bash
# environment (once)
python3.12 -m venv .venv
.venv/bin/pip install -r requirements.txt

# run all tests (pyproject puts src/ on the path -> `import componentd`)
.venv/bin/python -m pytest tests/ -q

# train the anomaly model (fast, local)
.venv/bin/python scripts/train_anomaly.py

# train the fusion model -> use notebooks/colab_train.ipynb on Colab GPU

# run the API server (8010, not 8000 - see server/main.py comment on why:
# 8000 is a common clash point with other local dev servers)
.venv/bin/uvicorn server.main:app --host 0.0.0.0 --port 8010

# frontend demo (separate terminal, API server must already be running)
cd clients/web
npm install
npm run dev
```

## Frontend

`clients/web/` is the panel-demo web UI, ported from the PP1 `cognify-ui`
React app and rewired to the v2 API contract (session-id based, single
`/full-session` call instead of three separate legacy calls). The final
product is a mobile app; this web UI exists to demonstrate the component
end to end. Point it at a non-default server with
`VITE_API_BASE=http://<host>:<port> npm run dev`.

## Training pipeline (two stages)

1. `scripts/extract_features.py` - run the frozen encoder + prosody over
   every clip once, cache to `.npz` (slow; Colab GPU).
2. `scripts/train_fusion.py` - train the fusion model on the cache
   (fast; minutes).

## Data

Never committed to git. `data/wav/meld` + `data/metadata_meld.csv` are
built by `src/componentd/datasets/meld.py`; acted sets by
`src/componentd/datasets/acted.py`; EmoTa (Tamil) by
`src/componentd/datasets/emota.py` once access is granted.

## Docs

- `docs/ARCHITECTURE.md` - design decisions, the 5 layers, the cross-modal contract
- `docs/DEPLOYMENT.md` - how to run the server + demo, and Component B integration
- `docs/ABLATION_STUDY.md` - PP1 -> PP2 model comparison for the panel
- `docs/PROJECT_HANDOFF.md` - full working context / status
