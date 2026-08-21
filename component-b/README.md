# Component B — HRV Stress Inference

Real-time 4-level stress inference from wearable PPG, feeding an adaptive VR
meditation system.

## Design principle

`src/componentb/` is the **single source of truth**. Both the research notebooks
and the live server import from it. Nothing is reimplemented in another language
or another file — this prevents training-serving skew, where live predictions
silently drift from validated results.

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

Ships the **60-beat causal MS-CGCA 2-way ensemble**: XGBoost over 25 engineered
features blended with a population Multi-Scale Circadian-Guided Cross-Attention
network, at `w_xgb = 0.15`, `w_cnn = 0.85`.

Windows are ~45 s (60 beats), baselines are past-only causal EWMA, and each
window is labeled at its **last beat** — the model predicts the present from the
past only, with no future lookahead.

| Metric | Value |
| --- | --- |
| Macro F1 | 0.5923 (5-seed estimate 0.5925 ± 0.0129) |
| Quadratic κ | 0.7525 (5-seed estimate 0.7755 ± 0.0174) |
| Accuracy | 0.8241 |
| Within-1 accuracy | 0.9366 |
| Severe errors (\|e\| ≥ 2) | 0.0634 |

The blend weight is chosen per export rather than fixed — an earlier export of the
same notebook shipped `0.20/0.80` at F1 0.5970. The grid optimum is a broad
plateau, so the pair moves between runs without meaningfully changing performance.
Read it from `model_config.json`, never from memory. See `docs/ARCHITECTURE.md` §3.

The equivalent non-causal offline model reaches F1 ≈ 0.682. **The ~0.090 gap is
the measured cost of causal, deployable inference** — the live pipeline does not
recover offline performance, and does not claim to.

See `docs/ARCHITECTURE.md` for how every figure was measured, which alternatives
were compared, and which older numbers were withdrawn.

## Output format

Each prediction carries the stress decision plus the raw physiology behind it:

```json
{
  "timestamp": 1787282898.4,
  "heartRate": 78.4,
  "rmssd": 34.1,
  "sdnn": 42.0,
  "stress": {
    "mode": "band",
    "level_low": 1,
    "level_high": 2,
    "label": "mild-to-moderate",
    "confidence": 0.10,
    "adjacent": true,
    "probabilities": {"relaxed": 0.08, "mild": 0.40, "moderate": 0.50, "high": 0.02},
    "continuous_score": 1.46
  },
  "signalQuality": 0.92,
  "windowStart": 1787282838.4,
  "windowEnd": 1787282898.4
}
```

When the model *is* confident enough to separate two adjacent levels, `stress.mode`
is `"point"` and `stress.level` replaces `level_low`/`level_high`.

- `rmssd`/`sdnn` are milliseconds and `heartRate` is bpm — real units, not the
  scaled values the model consumes.
- `confidence` is the **margin** between the top two classes, not the top
  probability. Below `CONFIDENCE_TAU` you get a band.
- `signalQuality` is the fraction of the window's heartbeats that arrived usable
  from the watch — **heartbeat-data quality, not BLE or network signal strength**.
- `continuous_score` is `sum(i * p_i)`, a derived convenience value.

`stress.mode`, `stress.level` and `stress.label` are authoritative.
`probabilities` and `continuous_score` are supplementary — **do not re-derive a
label from either**, as that bypasses the confidence gate. Full field definitions
in `docs/ARCHITECTURE.md` §6.
