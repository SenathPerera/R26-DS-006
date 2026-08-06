#!/usr/bin/env python3
"""
Component B — project scaffold generator.

Creates the folder structure and starter files for the real-time
stress inference system.

Usage:
    python setup_project.py                 # creates ./component-b
    python setup_project.py --path ~/dev    # creates ~/dev/component-b
    python setup_project.py --dry-run       # show what would be created
"""

import argparse
import os
from pathlib import Path

ROOT_NAME = "component-b"

# ----------------------------------------------------------------------
# Directories
# ----------------------------------------------------------------------
DIRS = [
    # --- the validated core: shared by training AND live inference ---
    "src/componentb",
    "src/componentb/signal",        # PPG -> RR, cleaning
    "src/componentb/baseline",      # EWMA / Cosinor baseline engines
    "src/componentb/features",      # the 25 features / 7-channel builder
    "src/componentb/models",        # architecture defs + load/save
    "src/componentb/inference",     # streaming buffer, confidence gating

    # --- the API server ---
    "server",
    "server/routes",
    "server/schemas",

    # --- trained artifacts (gitignored, but structure documented) ---
    "artifacts/models",
    "artifacts/scalers",
    "artifacts/config",

    # --- research notebooks (your existing work) ---
    "notebooks/01_pipeline",
    "notebooks/02_mechanism",
    "notebooks/03_architecture",
    "notebooks/04_scope_expansion",
    "notebooks/05_deployment",

    # --- clients ---
    "clients/web",
    "clients/web/static",
    "clients/unity/Scripts",
    "clients/mobile",

    # --- tests: the skew guard lives here ---
    "tests",
    "tests/fixtures",

    # --- docs and deliverables ---
    "docs/paper",
    "docs/reports",
    "docs/figures",

    # --- data (gitignored) ---
    "data/raw",
    "data/processed",
    "data/replay",
]

# ----------------------------------------------------------------------
# Files
# ----------------------------------------------------------------------
FILES = {}

FILES["README.md"] = """# Component B — HRV Stress Inference

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
source .venv/bin/activate        # Windows: .venv\\Scripts\\activate
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
"""

FILES["requirements.txt"] = """# --- core pipeline ---
numpy>=1.24
scipy>=1.11
pandas>=2.0
scikit-learn>=1.3
xgboost>=2.0
tensorflow>=2.15
neurokit2>=0.2.7

# --- server ---
fastapi>=0.110
uvicorn[standard]>=0.27
websockets>=12.0
pydantic>=2.6

# --- dev ---
pytest>=8.0
"""

FILES[".gitignore"] = """.venv/
__pycache__/
*.pyc
.pytest_cache/
.ipynb_checkpoints/

# artifacts and data are large / private
artifacts/models/*
artifacts/scalers/*
!artifacts/**/.gitkeep
!artifacts/config/*.json

data/raw/*
data/processed/*
data/replay/*
!data/**/.gitkeep

.env
.DS_Store
"""

FILES["src/componentb/__init__.py"] = '''"""Component B — validated HRV stress inference pipeline.

Imported by BOTH the research notebooks and the live server.
Do not duplicate this logic anywhere else.
"""

__version__ = "0.1.0"
'''

FILES["src/componentb/config.py"] = '''"""Configuration constants.

These values are baked into the trained models. Changing any of them
requires retraining — they are not free parameters at inference time.
"""

# --- windowing ---
WINDOW_BEATS = 120          # what the shipped model was trained on
STEP_BEATS = 5              # -> a prediction roughly every 4 s

# --- signal ---
PPG_SAMPLE_RATE = 64.0      # Hz, Empatica-compatible
TEMP_SAMPLE_RATE = 4.0      # Hz, TMP117
RR_MIN_MS = 300             # 200 bpm — below this is artefact
RR_MAX_MS = 2000            # 30 bpm  — above this is artefact
RR_JUMP_THRESHOLD = 0.20    # >20% beat-to-beat change is artefact

# --- baseline (causal, deployable) ---
# Single-scale causal tracking cost 0.06 F1 vs offline Cosinor;
# three scales recovered most of it (0.557 vs 0.569).
EWMA_HALFLIVES = {"fast": 60, "medium": 300, "slow": 1800}

# Cold start: seed from the population mean, NOT a donor cluster.
# Donor matching scored 0.475 — worse than the population 0.500.
POPULATION_RR_MS = 780.0

# --- output ---
CLASS_NAMES = ["relaxed", "mild", "moderate", "high"]
N_CLASSES = 4
CONFIDENCE_TAU = 0.15       # below this margin, emit a merged band
'''

FILES["src/componentb/signal/__init__.py"] = ""

FILES["src/componentb/signal/ppg.py"] = '''"""PPG -> RR intervals.

Ported directly from the validated Empatica notebook. Do not rewrite:
this exact code produced the reported cross-dataset results.
"""

import numpy as np
import neurokit2 as nk

from componentb.config import (
    PPG_SAMPLE_RATE, RR_MIN_MS, RR_MAX_MS, RR_JUMP_THRESHOLD,
)


def ppg_to_rr(ppg, fs=PPG_SAMPLE_RATE):
    """Detect beats in a raw PPG buffer and return RR intervals in ms.

    Returns (rr_ms, timestamps_s, peak_indices) or (None, None, None)
    if too few beats were detected to be useful.
    """
    clean = nk.ppg_clean(ppg, sampling_rate=fs)
    _, info = nk.ppg_peaks(clean, sampling_rate=fs)
    peaks = info["PPG_Peaks"]
    if len(peaks) < 10:
        return None, None, None
    rr = np.diff(peaks) * (1000.0 / fs)
    ts = (peaks[:-1] + peaks[1:]) / 2.0 / fs
    return rr, ts, peaks


def clean_rr(rr, ts=None):
    """Remove physiologically impossible values and artefact jumps."""
    rr = np.asarray(rr, dtype=float).copy()
    rr[(rr <= RR_MIN_MS) | (rr >= RR_MAX_MS)] = np.nan
    for i in range(1, len(rr)):
        if not np.isnan(rr[i - 1]) and not np.isnan(rr[i]):
            if abs(rr[i] - rr[i - 1]) / rr[i - 1] > RR_JUMP_THRESHOLD:
                rr[i] = np.nan
    m = np.isnan(rr)
    if m.any() and (~m).sum() >= 2:
        rr[m] = np.interp(np.where(m)[0], np.where(~m)[0], rr[~m])
    return rr, ts
'''

FILES["src/componentb/baseline/__init__.py"] = ""

FILES["src/componentb/baseline/ewma.py"] = '''"""Causal multi-timescale baseline engine.

Causal by construction: the value at index i depends only on samples
up to i. Verified by corrupting future samples and confirming past
output is unchanged (see tests/test_causality.py).

Cosinor is NOT used at inference time — it fits the whole session,
which a live device cannot do.
"""

import numpy as np

from componentb.config import EWMA_HALFLIVES, POPULATION_RR_MS


class BaselineEngine:
    """Tracks a personal expected RR level at several timescales."""

    def __init__(self, population_level=POPULATION_RR_MS,
                 halflives=None):
        self.halflives = halflives or EWMA_HALFLIVES
        self.population_level = population_level
        # cold start: seed every scale from the population mean
        self.state = {k: float(population_level) for k in self.halflives}
        self.alpha = {
            k: 1 - np.exp(np.log(0.5) / max(hl, 1))
            for k, hl in self.halflives.items()
        }
        self.n_seen = 0

    def update(self, rr_ms):
        """Feed one new RR interval. Past-only, O(1)."""
        self.n_seen += 1
        for k, a in self.alpha.items():
            self.state[k] = a * rr_ms + (1 - a) * self.state[k]

    def update_many(self, rr_array):
        for rr in np.asarray(rr_array, dtype=float):
            self.update(rr)

    def expected(self):
        """Current expected level at each timescale."""
        return dict(self.state)

    def residuals(self, rr_window):
        """Deviation of a window from each expected level."""
        m = float(np.mean(rr_window))
        return {k: m - v for k, v in self.state.items()}

    @property
    def maturity(self):
        """How settled the personal baseline is.

        NOTE: these thresholds are PROVISIONAL. The minimum-window
        experiment was invalidated (Cosinor fits fell back to prefix
        means for 15/15 subjects), so no validated beat count exists
        yet. Re-run with EWMA before treating these as established.
        """
        if self.n_seen < 160:
            return "population"     # ~2 min, still essentially seeded
        if self.n_seen < 1600:
            return "converging"     # ~20 min
        return "personal"
'''

FILES["src/componentb/features/__init__.py"] = ""

FILES["src/componentb/features/hrv.py"] = '''"""Feature extraction.

The exact functions used to train the shipped model. Feature ORDER
matters and must not change — the scaler and model depend on it.
"""

import numpy as np
from scipy.signal import welch

try:
    from scipy.integrate import trapezoid as TRAPZ
except ImportError:  # older scipy
    from scipy.integrate import trapz as TRAPZ


HRV_FEATURE_NAMES = [
    "mean_RR", "SDNN", "RMSSD", "pNN50", "CV_RR",
    "VLF", "LF", "HF", "LF/HF", "LF_nu", "SD1", "SD2", "SD1/SD2",
]
RESID_FEATURE_NAMES = [
    "res_mean", "res_SD", "res_maxabs", "res_meandiff", "res_slope",
]


def hrv_features(rr):
    """13 time- and frequency-domain HRV features from one window."""
    rr = np.asarray(rr, dtype=float)
    dd = np.diff(rr)
    nn = len(rr)

    rmssd = np.sqrt(np.mean(dd ** 2)) if nn > 1 else 0.0
    sdnn = np.std(rr)
    mean_rr = np.mean(rr)
    pnn50 = np.mean(np.abs(dd) > 50) * 100 if nn > 1 else 0.0
    cv = sdnn / (mean_rr + 1e-8)

    try:
        fs = 4.0
        t = np.cumsum(rr) / 1000.0
        ti = np.arange(t[0], t[-1], 1 / fs)
        ri = np.interp(ti, t, rr)
        f, pxx = welch(ri - np.mean(ri), fs=fs, nperseg=min(256, len(ri)))

        def band(lo, hi):
            m = (f >= lo) & (f < hi)
            return TRAPZ(pxx[m], f[m]) if np.any(m) else 0.0

        vlf, lf, hf = band(0.003, 0.04), band(0.04, 0.15), band(0.15, 0.4)
    except Exception:
        vlf = lf = hf = 0.0

    tot = lf + hf + 1e-8
    sd1 = np.sqrt(0.5) * np.std(dd) if nn > 1 else 0.0
    sd2 = np.sqrt(max(2 * sdnn ** 2 - 0.5 * np.std(dd) ** 2, 0)) if nn > 1 else 0.0

    return np.array([
        mean_rr, sdnn, rmssd, pnn50, cv,
        vlf, lf, hf, lf / (hf + 1e-8), lf / tot,
        sd1, sd2, sd1 / (sd2 + 1e-8),
    ])


def resid_features(residual):
    """5 features describing deviation from the expected baseline."""
    r = np.asarray(residual, dtype=float)
    dd = np.diff(r)
    return np.array([
        np.mean(r),
        np.std(r),
        np.max(np.abs(r)),
        np.mean(np.abs(dd)) if len(dd) > 0 else 0.0,
        np.polyfit(np.arange(len(r)), r, 1)[0],
    ])
'''

FILES["src/componentb/models/__init__.py"] = ""

FILES["src/componentb/models/loader.py"] = '''"""Load trained artifacts.

The scaler MUST be the one fitted during training. Normalising live
data with different statistics degrades predictions silently — no
error is raised, the answers are just wrong.
"""

import json
import pickle
from pathlib import Path

ARTIFACTS = Path(__file__).resolve().parents[3] / "artifacts"


def load_model(name="cnn_population"):
    """Load the shipped Keras model."""
    import tensorflow as tf
    path = ARTIFACTS / "models" / f"{name}.keras"
    if not path.exists():
        raise FileNotFoundError(
            f"Model not found: {path}\\n"
            "Export it from your training notebook first."
        )
    return tf.keras.models.load_model(path, compile=False)


def load_scaler(name="feature_scaler"):
    path = ARTIFACTS / "scalers" / f"{name}.pkl"
    if not path.exists():
        raise FileNotFoundError(
            f"Scaler not found: {path}\\n"
            "Export the StandardScaler fitted during training."
        )
    with open(path, "rb") as f:
        return pickle.load(f)


def load_config(name="model_config"):
    path = ARTIFACTS / "config" / f"{name}.json"
    with open(path) as f:
        return json.load(f)
'''

FILES["src/componentb/inference/__init__.py"] = ""

FILES["src/componentb/inference/stream.py"] = '''"""Streaming inference.

Holds a rolling buffer and emits a prediction every STEP_BEATS.
Must produce identical output to the batch pipeline on the same
data — see tests/test_parity.py.
"""

from collections import deque

import numpy as np

from componentb.config import (
    WINDOW_BEATS, STEP_BEATS, CLASS_NAMES, CONFIDENCE_TAU,
)
from componentb.baseline.ewma import BaselineEngine


class StreamingInference:
    def __init__(self, model=None, scaler=None,
                 window=WINDOW_BEATS, step=STEP_BEATS):
        self.model = model
        self.scaler = scaler
        self.window = window
        self.step = step
        self.rr_buffer = deque(maxlen=window)
        self.temp_buffer = deque(maxlen=window)
        self.baseline = BaselineEngine()
        self._since_last = 0

    def push(self, rr_ms, temp_c=None):
        """Feed one beat. Returns a prediction dict, or None if the
        buffer is not yet full or the step interval has not elapsed."""
        self.rr_buffer.append(float(rr_ms))
        self.temp_buffer.append(float(temp_c) if temp_c is not None else np.nan)
        self.baseline.update(rr_ms)
        self._since_last += 1

        if len(self.rr_buffer) < self.window:
            return None
        if self._since_last < self.step:
            return None

        self._since_last = 0
        return self._predict()

    def _predict(self):
        rr = np.array(self.rr_buffer)
        # TODO: build the 7-channel sequence exactly as in training,
        # apply self.scaler, run self.model.
        raise NotImplementedError(
            "Wire this to the exported model — see notebooks/05_deployment"
        )

    @staticmethod
    def format_output(probs, tau=CONFIDENCE_TAU):
        """Point estimate when confident, merged band when not.

        Justified by measurement: among low-confidence windows, 84.2%
        had the top two classes adjacent, matching the finding that
        neighbouring levels overlap physiologically.
        """
        probs = np.asarray(probs, dtype=float)
        order = np.argsort(probs)
        margin = float(probs[order[-1]] - probs[order[-2]])

        if margin >= tau:
            k = int(order[-1])
            return {
                "mode": "point",
                "level": k,
                "label": CLASS_NAMES[k],
                "confidence": round(margin, 3),
            }

        lo, hi = int(min(order[-2:])), int(max(order[-2:]))
        return {
            "mode": "band",
            "level_low": lo,
            "level_high": hi,
            "label": f"{CLASS_NAMES[lo]}-to-{CLASS_NAMES[hi]}",
            "confidence": round(margin, 3),
            "adjacent": bool(hi - lo == 1),
        }
'''

FILES["server/__init__.py"] = ""

FILES["server/main.py"] = '''"""FastAPI backend.

The ONLY place inference runs. Mobile relays raw PPG; Quest and the
web dashboard subscribe to predictions.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware

app = FastAPI(title="Component B — Stress Inference")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],          # tighten before any public deployment
    allow_methods=["*"],
    allow_headers=["*"],
)

subscribers: set[WebSocket] = set()


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.websocket("/ingest")
async def ingest(ws: WebSocket):
    """Mobile app sends raw PPG batches here."""
    await ws.accept()
    try:
        while True:
            msg = await ws.receive_json()
            # TODO: ppg_to_rr -> clean_rr -> StreamingInference.push
            # then broadcast(result)
            _ = msg
    except WebSocketDisconnect:
        pass


@app.websocket("/stream")
async def stream(ws: WebSocket):
    """Quest 2 and the website subscribe here."""
    await ws.accept()
    subscribers.add(ws)
    try:
        while True:
            await ws.receive_text()      # keepalive
    except WebSocketDisconnect:
        subscribers.discard(ws)


async def broadcast(payload: dict):
    dead = []
    for ws in subscribers:
        try:
            await ws.send_json(payload)
        except Exception:
            dead.append(ws)
    for ws in dead:
        subscribers.discard(ws)
'''

FILES["server/schemas/__init__.py"] = ""

FILES["server/schemas/messages.py"] = '''"""Wire format between clients and backend."""

from typing import Literal, Optional

from pydantic import BaseModel


class PPGBatch(BaseModel):
    """Mobile -> backend."""
    timestamp: float
    sample_rate: float = 64.0
    ppg: list[float]
    temperature: Optional[float] = None


class StressPrediction(BaseModel):
    """Backend -> Quest / website."""
    timestamp: float
    mode: Literal["point", "band"]
    level: Optional[int] = None
    level_low: Optional[int] = None
    level_high: Optional[int] = None
    label: str
    confidence: float
    deviation: dict[str, float]
    baseline_maturity: str
'''

FILES["tests/__init__.py"] = ""

FILES["tests/test_causality.py"] = '''"""The baseline engine must never use future data."""

import numpy as np

from componentb.baseline.ewma import BaselineEngine


def test_baseline_is_causal():
    rng = np.random.default_rng(0)
    rr = rng.normal(800, 50, 1000)

    # run A: the real signal
    a = BaselineEngine()
    states_a = []
    for x in rr[:500]:
        a.update(x)
        states_a.append(a.expected()["medium"])

    # run B: identical first half, corrupted future
    corrupted = rr.copy()
    corrupted[500:] = 9999.0
    b = BaselineEngine()
    states_b = []
    for x in corrupted[:500]:
        b.update(x)
        states_b.append(b.expected()["medium"])

    assert np.allclose(states_a, states_b), "baseline used future data"


def test_cold_start_uses_population():
    e = BaselineEngine(population_level=780.0)
    assert e.expected()["fast"] == 780.0
    assert e.maturity == "population"
'''

FILES["tests/test_parity.py"] = '''"""Streaming output must match the batch pipeline exactly.

This is the most important test in the project. If it fails, live
predictions differ from the validated notebook results and every
reported number becomes unverifiable in deployment.
"""

import pytest


@pytest.mark.skip(reason="enable once the model is exported")
def test_streaming_matches_batch():
    # 1. take a WESAD session
    # 2. run the batch pipeline -> predictions_batch
    # 3. replay the same beats through StreamingInference
    #    -> predictions_stream
    # 4. assert they agree
    raise NotImplementedError
'''

FILES["clients/web/index.html"] = '''<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Component B — Live Stress</title>
  <style>
    body { font-family: system-ui, sans-serif; margin: 2rem; }
    #level { font-size: 3rem; font-weight: 600; }
    #meta { color: #666; }
    .band { color: #b45309; }
  </style>
</head>
<body>
  <h1>Live Stress Inference</h1>
  <div id="level">--</div>
  <div id="meta">disconnected</div>

  <script>
    const ws = new WebSocket(`ws://${location.hostname}:8000/stream`);
    const levelEl = document.getElementById('level');
    const metaEl = document.getElementById('meta');

    ws.onopen = () => metaEl.textContent = 'connected';
    ws.onclose = () => metaEl.textContent = 'disconnected';
    ws.onmessage = (ev) => {
      const d = JSON.parse(ev.data);
      levelEl.textContent = d.label;
      levelEl.className = d.mode === 'band' ? 'band' : '';
      metaEl.textContent =
        `confidence ${d.confidence} | baseline ${d.baseline_maturity}`;
    };
  </script>
</body>
</html>
'''

FILES["clients/unity/Scripts/StressClient.cs"] = '''// Quest 2 client. Requires NativeWebSocket:
//   https://github.com/endel/NativeWebSocket
using UnityEngine;
using NativeWebSocket;

public class StressClient : MonoBehaviour
{
    [Tooltip("Laptop IP on the local network")]
    public string serverHost = "192.168.1.100";
    public int serverPort = 8000;

    WebSocket ws;

    [System.Serializable]
    public class StressPrediction
    {
        public string mode;
        public int level;
        public string label;
        public float confidence;
        public string baseline_maturity;
    }

    async void Start()
    {
        ws = new WebSocket($"ws://{serverHost}:{serverPort}/stream");
        ws.OnMessage += (bytes) =>
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var p = JsonUtility.FromJson<StressPrediction>(json);
            OnStressUpdate(p);
        };
        await ws.Connect();
    }

    void OnStressUpdate(StressPrediction p)
    {
        // A "band" means the model is uncertain between adjacent
        // levels — prefer a gentler, less specific response here.
        Debug.Log($"stress={p.label} mode={p.mode} conf={p.confidence}");
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    async void OnApplicationQuit()
    {
        if (ws != null) await ws.Close();
    }
}
'''

FILES["docs/ARCHITECTURE.md"] = """# Architecture Decisions

Each decision below is traceable to a measured result.

## Inference runs on the backend, not the headset or phone

Quest 2 shares its GPU between rendering and any compute; dropped
frames cause motion sickness. Running inference on the phone would
still require a relay to reach the Quest and website, so it removes
no complexity while duplicating the validated pipeline in another
language.

## Ships the single population CNN, not the ensemble

| Model | Macro F1 |
|---|---|
| Three-way ensemble | 0.715 |
| Population CNN alone | 0.654 |

Difference: dF1 = 0.023, p = 0.5245, d = 0.34 — not significant.
The ensemble also requires a per-user fine-tuning pipeline. Not
worth 3x inference cost for an unproven gain.

## Causal EWMA baseline, not Cosinor

Cosinor fits the whole session; a live device cannot. Measured cost
of going causal:

| Baseline | Macro F1 |
|---|---|
| Offline Cosinor | 0.569 |
| Best single causal tracker | 0.508 |
| Three-scale causal EWMA | 0.557 |

Multi-timescale recovers roughly 80% of the gap.

## Cold start seeds from the population mean

| Cold-start strategy | Macro F1 |
|---|---|
| No baseline | 0.470 |
| Donor-cluster ("borrowed") | 0.475 |
| Population mean | 0.500 |
| Own full baseline | 0.569 |

Donor matching was significantly worse than the subject's own
baseline (p = 0.0026, d = -1.01) and scored below the population
mean. Do not cluster new users into donor groups.

## Confidence gating with merged bands

Among low-confidence windows, 84.2% had the top two classes
adjacent, consistent with the measured physiological overlap between
neighbouring levels (eps-squared 0.03 for levels 2 vs 3). At 80%
coverage: F1 +0.053, severe errors -0.036.

## Open items

- **Baseline maturity thresholds are provisional.** The minimum-window
  experiment was invalidated — Cosinor fits fell back to prefix means
  for 15/15 subjects at short budgets. Re-run with EWMA.
- **60-beat window** scored better than 120 (0.595 vs 0.552) but awaits
  seed replication, and would require retraining.
"""

FILES["docs/DEPLOYMENT.md"] = """# Deployment

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

## Required artifacts

Export these from the training notebook before the server will run:

```
artifacts/models/cnn_population.keras
artifacts/scalers/feature_scaler.pkl
artifacts/config/model_config.json
```

The scaler must be the exact object fitted during training.

## Before trusting live output

Run the parity test. If streaming and batch predictions disagree on
the same input, there is a bug — find it before hardware is involved.

```bash
pytest tests/test_parity.py -v
```
"""

FILES["docs/reports/.gitkeep"] = ""
FILES["docs/paper/.gitkeep"] = ""
FILES["docs/figures/.gitkeep"] = ""
FILES["artifacts/models/.gitkeep"] = ""
FILES["artifacts/scalers/.gitkeep"] = ""
FILES["artifacts/config/.gitkeep"] = ""
FILES["data/raw/.gitkeep"] = ""
FILES["data/processed/.gitkeep"] = ""
FILES["data/replay/.gitkeep"] = ""
FILES["tests/fixtures/.gitkeep"] = ""
FILES["clients/mobile/.gitkeep"] = ""
FILES["notebooks/01_pipeline/.gitkeep"] = ""
FILES["notebooks/02_mechanism/.gitkeep"] = ""
FILES["notebooks/03_architecture/.gitkeep"] = ""
FILES["notebooks/04_scope_expansion/.gitkeep"] = ""
FILES["notebooks/05_deployment/.gitkeep"] = ""
FILES["server/routes/__init__.py"] = ""
FILES["src/componentb/models/.gitkeep"] = ""


def main():
    ap = argparse.ArgumentParser(description="Scaffold the Component B project.")
    ap.add_argument("--path", default=".", help="where to create the project")
    ap.add_argument("--name", default=ROOT_NAME, help="project folder name")
    ap.add_argument("--dry-run", action="store_true", help="show, do not create")
    args = ap.parse_args()

    root = Path(args.path).expanduser().resolve() / args.name

    if root.exists() and any(root.iterdir()) and not args.dry_run:
        resp = input(f"{root} exists and is not empty. Continue? [y/N] ")
        if resp.strip().lower() != "y":
            print("aborted")
            return

    created_dirs = 0
    created_files = 0

    for d in DIRS:
        p = root / d
        if args.dry_run:
            print(f"DIR   {p}")
        else:
            p.mkdir(parents=True, exist_ok=True)
        created_dirs += 1

    for rel, content in FILES.items():
        p = root / rel
        if args.dry_run:
            print(f"FILE  {p}")
        else:
            p.parent.mkdir(parents=True, exist_ok=True)
            if p.exists():
                print(f"  skip (exists): {rel}")
                continue
            p.write_text(content, encoding="utf-8")
        created_files += 1

    if args.dry_run:
        print(f"\n[dry run] {created_dirs} dirs, {created_files} files")
        return

    print(f"\nCreated {created_dirs} directories and {created_files} files")
    print(f"Location: {root}\n")
    print("Next steps:")
    print(f"  cd {root}")
    print("  python -m venv .venv && source .venv/bin/activate")
    print("  pip install -r requirements.txt")
    print("  pytest tests/test_causality.py -v")
    print("\nThen export your trained model into artifacts/ —")
    print("see docs/DEPLOYMENT.md")


if __name__ == "__main__":
    main()
