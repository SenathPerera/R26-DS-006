# CogniVoice — Component D: Master Document

The single source of truth for this project. If you forget everything else,
read this file. It explains what the component is, why each design decision
was made, how each of the five layers works, and where things stand.

---

## 1. What this component does (one paragraph)

Component D is the **voice-based stress detection** part of the COGNIFY VR
meditation system. A user speaks for ~30 seconds before a meditation session
and ~30 seconds after. Component D listens to both recordings and answers:
**how stressed does this person sound, did the session help, does that agree
with their heart-rate data, and is this session unusual for them?** It runs as
a server (API); the VR headset and mobile app just send audio and receive JSON.

---

## 2. The problem we are solving (why PP2 exists)

In PP1 the stress model was a **pretrained model used directly** with a
hand-written lookup table converting emotions to stress. The panel's feedback:
*directly using a pretrained model is not a contribution — you must build and
train something yourself.* A second problem surfaced in testing: models trained
on **acted** speech (actors performing anger) scored well on acted test sets but
**failed on real voices**. The real bottleneck was the data, not the encoder.

**PP2 answer:** train our own fusion model on more natural speech, combining a
frozen emotion encoder with a trainable acoustic branch and a trainable head.
That head is *our* contribution — trained, not a lookup table.

---

## 3. The big picture (data flow)

```
  User speaks (pre-session)                 User speaks (post-session)
          │                                          │
          ▼                                          ▼
  ┌───────────────┐                          ┌───────────────┐
  │ LAYER 1       │  is the audio usable?    │ LAYER 1       │
  │ quality gate  │  (noise, clipping, SNR)  │ quality gate  │
  └───────┬───────┘                          └───────┬───────┘
          ▼                                          ▼
  ┌───────────────┐                          ┌───────────────┐
  │ LAYER 2       │  stress score 0-10       │ LAYER 2       │
  │ fusion model  │  (THE trained model)     │ fusion model  │
  └───────┬───────┘                          └───────┬───────┘
          │                                          │
          └──────────────────┬───────────────────────┘
                             ▼
                    ┌─────────────────┐
                    │ LAYER 3         │  did stress go down? is the
                    │ pre/post compare│  change real or just noise?
                    └────────┬────────┘
                             ▼
                    ┌─────────────────┐   HRV from Component B
                    │ LAYER 4         │◄────(wearable heart data)
                    │ cross-modal     │   do voice and body agree?
                    └────────┬────────┘
                             ▼
                    ┌─────────────────┐
                    │ LAYER 5         │  is this session abnormal
                    │ anomaly detect  │  compared to this user's history?
                    └────────┬────────┘
                             ▼
              Final JSON  →  mobile app + Component C (Unity VR)
              {stress_level, confidence, improved, validated, anomaly}
```

---

## 4. The five layers, explained one by one

### LAYER 1 — Ambient / quality gate
**File:** `src/layer1_quality.py` · **Uses a frozen pretrained model (Silero VAD),
plus DSP rules. Nothing trained by us here.**

**Job:** reject bad audio before it reaches Layer 2. A VR headset mic in a noisy
room, or someone talking in the background during the "stay silent" step,
produces garbage; we catch it here.

**Two separate checks, not one** (this distinction matters — reusing one check
for both was a real bug where a genuinely noisy room passed the silence step):
- **`check_ambient`** — the "please stay silent" step. Fails if the room is loud
  (RMS floor) **or if any human voice is detected** (background chatter, someone
  talking nearby).
- **`check_speech`** — the pre/post voice step. Fails if the clip is too quiet,
  too loud, clipped, or **does not contain enough actual speech** (needs ≥25%).

**The key tool — Voice Activity Detection (Silero VAD):** a small (~1.8 MB),
frozen, pretrained model whose only job is "is there a human voice here". Real
background noise (fans, traffic, chatter, hums) cannot be reliably told apart
from speech by hand-tuned spectral thresholds — the earlier version tried this
and let a noisy room through. VAD solves exactly that narrow sub-problem, the
same principle as Layer 2 using a frozen pretrained encoder. The `silero-vad`
package bundles the weights, so it runs offline with no download.

**Output:** `{ok: true/false, reasons: [...], metrics: {duration_sec, rms,
clip_ratio, speech_seconds, speech_fraction, speech_segments}}`

**Why we did not train anything here:** VAD is a solved problem with an excellent
free pretrained model; the surrounding decisions (loudness floor, clipping,
speech fraction) are explainable rules. Training our own would be strictly worse.

---

### LAYER 2 — Stress scoring (THE trained model, our contribution)
**Files:** `src/prosody_features.py`, `src/fusion_model.py`,
`src/inference_fusion.py` · **Trained model?** YES — this is the core.

**Job:** turn 30 seconds of speech into a stress score from 0 to 10.

**The architecture — two branches that get fused:**

```
        audio (16 kHz mono)
       ┌──────┴───────────────────────┐
       ▼                               ▼
  emotion2vec_plus_large         Prosody extractor
  (FROZEN, 300M params)          (parselmouth + librosa)
  "what emotion is in            "how is the voice behaving
   this voice?"                   physically?"
  → 1024 numbers                 → 21 numbers: F0 (pitch),
       │                          jitter, shimmer, HNR,
       │                          energy, speech rate, pauses
       └──────────────┬───────────────┘
                      ▼
             GATED FUSION LAYER          ← WE TRAIN THIS
             learns, per input, how much
             to trust each branch
                      ▼
             MLP HEAD                     ← WE TRAIN THIS
                      ▼
             valence + arousal            ← LEARNED (not a lookup table)
                      ▼
             stress = high arousal × negative valence
             → score 0-10 + confidence
```

**Why two branches (the key insight):**
- **emotion2vec** is pretrained on 42,500 hours of speech — it is excellent at
  emotion but its knowledge is mostly English/Chinese, and it can overfit to the
  "acted" style of studio datasets.
- **Prosody features** (pitch rising, voice trembling = jitter/shimmer, talking
  faster) are **physiological** — they happen under stress in *any language* and
  in *real* spontaneous speech. This branch is what makes the model robust to
  real voices and, later, transfer to Tamil and Sinhala.
- **Neither alone is enough.** Fusing them, with a head we train, is the
  contribution.

**Why valence/arousal instead of direct "stress":** almost no public dataset is
labelled "stressed". Lots are labelled with emotions, which map to **valence**
(pleasant↔unpleasant) and **arousal** (calm↔excited). We train the head to
predict those two numbers, then compute stress = high arousal + negative valence
(Russell's circumplex, a standard psychology model). This replaces PP1's
hand-written table with a learned function.

**Why emotion2vec is frozen:** we have limited data. Fine-tuning a 300M model
would overfit. We freeze it as a feature extractor and only train the small
fusion + head (~1-2M parameters) — fast, and it cannot overfit the big encoder.

**Output:** `{stress_score: 0-10, confidence, valence, arousal, gate_mean}`
(`gate_mean` near 1 = the model leaned on emotion embeddings; near 0 = it leaned
on prosody — useful for explaining decisions.)

---

### LAYER 3 — Pre/post comparison
**File:** `src/layer3_comparison.py` · **Trained model?** No — statistics.

**Job:** compare the pre-session and post-session stress scores and say whether
the session helped.

**How it works:** computes the delta (post − pre). Crucially, it does **not**
over-interpret tiny changes: a change smaller than a noise floor (widened when
the model's confidence is low) is reported as **"no reliable change"** instead
of fake precision. Real changes are labelled improved/worsened with a magnitude
(slight / moderate / strong).

**Output:** `{delta, direction, improved, magnitude, reliable, mean_confidence}`

**Why this matters:** a wellness app that claims "you improved 0.3 points!" from
noise is dishonest. This layer enforces intellectual honesty.

---

### LAYER 4 — Cross-modal validation (voice × heart rate)
**File:** `src/layer4_crossmodal.py` · **Trained model?** No — rule-based logic.

**Job:** check the voice-based stress against **HRV** (heart-rate variability)
from Component B's wearable. Two independent signals agreeing is far more
trustworthy than one alone.

**How it works:** HRV (RMSSD in milliseconds) is mapped to its own 0-10 stress
proxy (lower HRV = more stressed). Then it compares voice and HRV at both time
points and their trends, and classifies any disagreement into one of four types:
- **vocal_masking** — voice sounds calmer than the body actually is.
- **cognitive_persistence** — body recovered but the voice is still stressed.
- **baseline_divergence** — the two signals disagree throughout.
- **outcome_divergence** — they agreed before but disagree after.

**Component B integration:** Component B pushes HRV via the `/session-update`
API endpoint. For solo demos with no wearable, a `MockHRVProvider` stands in.

**Output:** `{validated, agreement, mismatch_type, voice, hrv}`

**This is also where your component's boundary with the HRV teammate lives:**
Component B *produces* HRV; Component D *consumes* it and owns all the
cross-modal fusion logic. That is why two "stress detectors" is not
duplication — this layer only exists because there are two signals.

---

### LAYER 5 — Longitudinal anomaly detection
**File:** `src/layer5_anomaly.py` · **Trained model?** YES — an autoencoder.

**Job:** over many sessions, flag when a session is abnormal *for that specific
user* — e.g. a sudden stress spike, or the app behaving oddly.

**How it works:** a **variational autoencoder (VAE)** learns to reconstruct
"normal" sessions from a probabilistic latent, with a decoder that predicts a
variance per feature. A new session is scored by its **reconstruction
probability** (An & Cho 2015) — the variance-normalised reconstruction error
averaged over latent samples — which is more principled than a plain
autoencoder's raw error (a feature the model is naturally unsure about is not
punished as harshly). High score = anomaly; Isolation Forest and One-Class SVM
are reported as baselines. The threshold is
**per-user** (each person's own baseline mean + 3 standard deviations) once they
have 5+ sessions; before that a global threshold is used (cold start).

**The 12 features** describe a whole session: pre/post stress, delta, confidences,
duration, HRV agreement, acoustic variance, ambient noise, session number, time
of day, days since last session.

**Output:** `{anomaly, anomaly_direction, severity, reasons, error, threshold, personalised}`
(`reasons` names which features drove the anomaly — explainable.)

**Why `anomaly_direction` exists:** reconstruction error alone cannot tell an
unusually *good* session (a much bigger stress drop than normal) apart from an
unusually *bad* one (stress rose sharply) — both just look like "far from
normal" to the autoencoder. A wellness app must never present a user's best
session as an alarming "severe anomaly", so the sign of `delta` resolves the
direction: `unusual_improvement` (delta ≤ 0) or `unusual_worsening` (delta > 0),
and `null` when the session is not anomalous at all. This was found and fixed
after the first live end-to-end test flagged a genuinely strong improvement
session (pre 8.76 → post 2.27) as "severe" with no way to tell it was good news.

**Current status:** trained on **simulated** sessions (cold start), because real
session history does not exist yet. It retrains on real data once sessions
accumulate. This is stated honestly, not hidden.

---

## 5. How it all ships (the API)

**File:** `api_server.py` (FastAPI). The client (VR headset / phone) only ever
talks to these endpoints:

| Endpoint | What it does | Layer |
|---|---|---|
| `GET /health` | which layers/models are loaded | all |
| `POST /ambient-check` | audio quality only | 1 |
| `POST /infer` | audio → stress score | 1+2 |
| `POST /compare` | pre vs post for a session | 3 |
| `POST /session-update` | Component B pushes HRV | 4 |
| `POST /cross-validate` | voice vs HRV | 4 |
| `POST /anomaly-check` | is this session abnormal | 5 |
| `POST /full-session` | everything combined → Component C payload | 3+4+5 |

If a trained model is missing, its endpoint returns a clean `503` instead of
crashing — so the system runs even before training is finished.

---

## 6. Why this runs on a server, not on the Quest 2

The Meta Quest 2 is a mobile chip already busy rendering VR. A 300M-parameter
encoder cannot run on it. It does not need to: the headset records audio and
sends it to the Component D server over Wi-Fi, which does all the heavy work and
returns a small JSON. This is standard client-server design and means we can
improve the models forever without touching the headset app.

---

## 7. Data plan

**English (now — the priority, must be 100% first):**
- **MELD** — ~13,700 conversational clips from the TV series *Friends*
  (naturalistic, closer to real speech than acted data). **Already downloaded,
  converted to wav, metadata built.** This is the main training set.
- **RAVDESS + CREMA-D + TESS** — acted English from PP1 (~11k clips). Used as
  auxiliary/comparison data, not the main source anymore.
- **MSP-Podcast** (licence requested) — fully natural podcast speech; retrain on
  it when access arrives.

**Tamil (later):** EmoTa dataset (936 clips, requested from Univ. of Moratuwa).
**Sinhala (later):** no public dataset exists → zero-shot evaluation, carried by
the language-independent prosody branch.

Order is deliberate: **finish English completely first**, then Tamil is just
"run the same pipeline with one more CSV", and Sinhala is evaluation-only.

---

## 8. How training works (the two-stage pipeline)

Encoders are slow, so we never run them twice:

1. **`scripts/extract_features.py`** — runs each clip through the frozen encoder
   + prosody once, caches everything to a `.npz` file. (Slow, run once.)
2. **`scripts/train_fusion.py`** — trains the fusion + head on the cached
   features. (Fast — minutes, even on a laptop.)

This is why the big one-time cost is feature extraction; every experiment after
that is quick.

---

## 9. Current status (PP2 progress + the core finding)

**Done:**
- All 5 layers written, 57 tests passing, pushed to GitHub.
- API server wiring all layers, with graceful handling of untrained models.
- **Shared preprocessing package** (`src/preprocessing/`): loudness normalisation,
  70 Hz high-pass, silence trim — applied identically at training and inference
  (one function, no train/serve skew). Loudness normalisation removes the
  recording-level confound when mixing studio (acted) and phone (WhatsApp) audio.
- **Two training runs + the key diagnostic finding** (see `docs/ABLATION_STUDY.md`):
  - Run 1 — MELD (noisy natural): flops on real voices.
  - Run 2 — clean acted (RAVDESS+CREMA-D+TESS, balanced, preprocessed): near-ceiling
    acted scores (test CCC v 0.86 / a 0.81, F1 0.92) **yet still flops on real
    voices in exactly the same way** — valence correct, **arousal collapsed**.
  - Two very different datasets, same failure → the bottleneck is **the
    categorical→V/A lookup table** (`config.EMOTION_VA`), not data quality. The
    "learned" head reproduces a lookup rather than real activation.
- Real-voice acid test now includes a **Sinhala zero-shot set** (collected clips,
  held out) — the arousal collapse reproduces across languages.
- **Layer 5 upgraded to a VAE** with reconstruction-probability scoring
  (An & Cho 2015) + Isolation Forest / One-Class SVM baselines; still cold-start
  on simulated sessions until real history exists.

**The fix the evidence points to:** real *continuous* valence/arousal labels
(dimensional corpora — IEMOCAP / MSP-Podcast) plugged into the same pipeline.
Because features are cached, this is a label swap + fast head retrain, no
architecture change. IEMOCAP access has been requested; the parser
(`src/datasets/iemocap.py`) is written and tested and ready to plug in.

**Why PP2 is already defensible without it:** the controlled ablation *proves*
the real-voice failure is caused by the label scheme, not the encoder,
architecture, or data quality — a genuine, panel-worthy diagnosis that stands on
its own. The dimensional-label retrain is the confirmation of the proposed fix.

---

## 10. Where everything lives

| What | Where |
|---|---|
| Code (source of truth) | `~/Projects/cognivoice-component-d` + GitHub |
| Datasets (never in git) | `data/` locally + KINGSTON pendrive backup |
| Trained models (never in git) | `models/` locally |
| This document | `docs/MASTER.md` |
| PP1 record | `docs/PP1_SUMMARY.md` |
| Old PP1 project | `/Volumes/KINGSTON/voice_stress_pipeline` (reference only) |
