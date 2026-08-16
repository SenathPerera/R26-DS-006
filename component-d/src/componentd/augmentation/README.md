# Domain augmentation — panel study notes

**Job:** make clean *studio* training audio resemble real *phone* recordings, so
the model learns to read stress through the degradations a real user's voice note
actually has.

**One-line answer for the panel:** *"My ablation showed every model — acted,
IEMOCAP — hedges toward neutral on real phone voices because all training audio
is clean studio audio. Augmentation degrades the studio clips (codec, noise,
reverb, phone bandwidth) so the model sees the acoustic domain it will be used
in — no new data needed."*

---

## Why this is the key lever now

The 4-way ablation (MELD → acted → IEMOCAP) proved the remaining bottleneck is
**not the labels** but the **acoustic domain gap**: studio-clean training vs
phone-recorded, compressed, noisy real voices. IEMOCAP even *worsened* it —
better labels, but still studio audio, so the model regressed to the mean on
phone clips. Augmentation attacks that gap directly.

## The four degradations

| Augment | Simulates | How |
|---|---|---|
| `add_background_noise` | a real room, not a booth | white noise at 8–25 dB SNR |
| `apply_reverb` | room acoustics | convolve with a short decaying impulse response |
| `codec_roundtrip` | WhatsApp/phone compression | encode→decode through lossy OGG Vorbis |
| `telephone_bandpass` | phone bandwidth | band-limit to ~300–3400 Hz |

`augment()` applies a **random subset** per clip, so each augmented copy is
different — the model sees many versions of the same voice under different
phone-like conditions.

## Where it sits in the pipeline

```
raw studio clip → AUGMENT (degrade to phone-like) → shared preprocessing
                                                     (condition) → encoder + prosody
```

- **Training clips only.** Validation/test clips are never augmented — we
  evaluate on clean audio so the metric isn't gamed.
- **Before** the shared preprocessing, so the degraded audio then goes through
  the exact same conditioning a real phone clip would at inference time.

## How to use

`scripts/extract_features.py --augment 2` adds 2 augmented copies of every
*train* clip (so training data ~3×). The copies carry the same labels.

## Files
- `augment.py` — the four degradations + `augment()`
- tested by `tests/test_augmentation.py`
