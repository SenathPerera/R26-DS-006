# Component B — HRV Stress Inference

Real-time 4-level stress inference from wearable PPG, feeding an
adaptive VR meditation system.

## Design principle

`src/componentb/` is the **single source of truth**. Both the research
notebooks and the live server import from it. Nothing is reimplemented
in another language or another file — this prevents training-serving
skew, where live predictions silently drift from validated results.

```
notebooks/  ──┐
              ├──> src/componentb/  <── the validated pipeline
server/     ──┘
```

## Architecture

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

Ships the **population CNN alone**, not the three-way ensemble.
The ensemble's advantage was not statistically significant
(dF1 = 0.023, p = 0.5245) and costs 3x the inference.
