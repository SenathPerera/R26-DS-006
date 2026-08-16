# Preprocessing — panel study notes

**Job:** turn raw audio (any source, any recording level) into a clean,
consistent waveform before it reaches the model. In ML research this step
decides whether the model learns the *real signal* or a *recording artefact*.

**One-line answer for the panel:** *"Every clip — studio-acted, TV, or a
phone voice note — is passed through the same conditioning function, so the
model sees stress, not the microphone."*

---

## The pipeline (Stage 0 → 2)

```
raw audio ─► decode ─► mono ─► 16 kHz ─► [ CONDITION ] ─► duration cap ─► model
                                             │
                        1a DC-offset removal │
                        1b high-pass 70 Hz   │  ← src/preprocessing/conditioning.py
                        1c silence trim      │
                        1d loudness normalise│
```

| Stage | What | Parameter | Why |
|---|---|---|---|
| 1a | Remove DC offset | subtract mean | centres the waveform; some mics add a constant bias |
| 1b | High-pass filter | Butterworth, 70 Hz, order 2 | speech F0 is above ~70 Hz; below is rumble/handling noise |
| 1c | Trim silence | `librosa.effects.trim`, top_db=30 | drops dead air at start/end **only** — internal pauses stay, because pausing patterns carry stress information |
| 1d | Loudness normalise | RMS → 0.05 (~-26 dBFS), anti-clip 0.99 | every clip at the same loudness (see confound below) |
| 2 | Duration | min 1.0 s (reject), max 35 s (trim) | reject fragments, cap very long clips |

## The confound this prevents (the important bit)

We train on **RAVDESS + CREMA-D + TESS** (clean studio) **+ WhatsApp voice
notes** (phone mic) — recorded at very different levels. The prosody branch
includes energy features (`rms_mean`, `rms_max`). **Without loudness
normalisation the model could learn "louder clip = stressed" — i.e. tell
datasets apart by microphone gain instead of by stress.** That is a
Clever-Hans confound, and a panel will ask *"how do you know it learned
stress and not recording level?"* Normalising every clip to the same RMS
removes the gain cue and forces the model to rely on pitch, voice quality,
and energy *dynamics* — the true stress signals.

*(Note: after normalisation absolute loudness is constant — that was the
confound — but energy **dynamics** like `rms_std` survive and still carry
arousal. We keep the signal, drop the artefact.)*

## The one rule: train == inference

`condition()` is imported by **both**:
- `scripts/extract_features.py` (training)
- `src/layer2_inference.py` (live scoring)

so a clip is treated identically in both. If they diverged, the model would
work in training and flop live (train/serve skew).

## How to use

```python
from src.preprocessing import preprocess_file, condition, prepare

audio, ok, reason = preprocess_file("clip.ogg")   # load + full pipeline
audio = condition(raw_array, sr)                    # array already in memory
```

## Files
- `conditioning.py` — the pipeline (`condition`, `prepare`, `preprocess_file`)
- `__init__.py` — package exports
- tested by `tests/test_preprocessing.py`
