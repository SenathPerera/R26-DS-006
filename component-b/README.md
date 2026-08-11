# Component B — HRV Stress Inference

Real-time 4-level stress inference from wearable PPG, feeding an adaptive VR meditation system.

## Design principle

`src/componentb/` is the **single source of truth**. Both the research notebooks and the live server import from it. Nothing is reimplemented in another language or another file — this prevents training-serving skew, where live predictions silently drift from validated results.

```text
notebooks/  ──┐
              ├──> src/componentb/  <── the validated pipeline
server/     ──┘
```

## Architecture

```text
Wearable (PPG + TMP117)
      | BLE
      v
  Mobile app          relays raw PPG, runs no model
      | WebSocket
      v
  Python backend      <- ALL causal feature extraction & inference happens here
      | WebSocket
   +--+--+
   v     v
 Quest  Website
```

## Quick start

```bash
python -m venv .venv
source .venv/bin/activate        # Windows: .venv\Scripts\activate
pip install -r requirements.txt

# verify streaming matches batch before anything else
pytest tests/test_parity.py -v

uvicorn server.main:app --reload --host 0.0.0.0 --port 8000
```

Clients connect to `ws://<laptop-ip>:8000/stream`.

## Model in production

Ships the **Shipped MS-CGCA 3-Way Nested Ensemble** (XGBoost + Population Multi-Scale Circadian-Guided Cross-Attention Deep Network + Personalised Fine-Tuned Head). Operating on **60-beat ultra-short windows (~45-second latency)** and **past-only causal EWMA baselines**, the live production engine achieves:

- **Macro F1:** `0.6708 – 0.6825` (mean `0.6766`)
- **Quadratic Kappa ($\kappa$):** `0.8386 – 0.8497`
- **Overall Accuracy:** `91.69% – 91.93%`

All features and deep sequence tensors are strictly past-only, ensuring 100% zero future-data leakage during live streaming. See `docs/ARCHITECTURE.md` for full benchmark sourcing and causal verification proofs.
