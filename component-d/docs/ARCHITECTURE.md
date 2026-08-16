# Architecture Decisions — Component D (Voice Stress)

Each decision below is traceable to a measured result in `docs/ABLATION_STUDY.md`.
Component D turns a ~30 s voice check-in (before and after a meditation session)
into a stress score, decides whether the session helped, cross-checks that
against Component B's heart signal, and flags anomalous sessions.

```text
   voice (before)          voice (after)
        │                       │
        ▼                       ▼
   L1 quality gate ───────► L2 stress model ──► valence/arousal → stress + confidence
                                 │
                                 ├──► L3 pre/post compare (did it help?)
                                 ├──► L4 cross-modal (voice × Component B HRV)
                                 └──► L5 anomaly (session-level VAE)
```

## 1. The stress model = frozen encoder + trainable head (two stages)

`src/componentd/layer2_*`. A large pretrained speech-emotion encoder
(`emotion2vec_plus_large`, 1024-d, **frozen**) supplies the emotional
representation; a small trainable branch adds prosody (F0, jitter, shimmer,
rate), a gated fusion combines them, and a learned head regresses
valence/arousal. Only the head + fusion + prosody branch train (~1–2 M params),
so training is a fast head-retrain on cached features — no GPU-days.

**Why frozen:** PP1 proved the encoder is the dominant factor (emotion2vec 84.7%
vs wav2vec2 60.9% on the same task). Freezing it keeps that strength and makes
the pipeline a two-stage cache: extract features once, retrain the head in
minutes. **Cost, stated honestly:** a frozen encoder cannot adapt to languages
it never saw — the root cause of the Sinhala limitation (§5).

## 2. Valence-primary scoring (the core PP2 finding)

Acted-speech metrics **invert** on real voices, and the reason is axis-specific:
across every checkpoint and both languages, **valence transfers but arousal
collapses** — quiet, internalised "freeze" stress reads as low-energy. So:

- **stress magnitude = `max(0, −valence)`** — driven by unpleasantness alone; a
  neutral voice maps to 0.
- **arousal names the _type_** (activated vs shutdown), never the magnitude, and
  is the axis cross-checked against B's HRV.
- **confidence = `|valence|`** — low exactly on the ambiguous/collapse clips.

This took the shipped model to 91.7% real-voice accuracy with **no retraining**,
purely by scoring on the reliable axis (`config.stress_from_va`).

## 3. Confidence-gated cross-modal validation (Layer 4)

`src/componentd/layer4_crossmodal.py`. Voice and heart are compared at both time
points and by trend. A disagreement is only asserted as a genuine
cognitive–physiological mismatch when **both** signals are confident
(`CONF_MIN = 0.4`); when the voice is uncertain it **defers to HRV** instead of
raising a false flag. This is the architectural answer to the arousal-collapse
limitation: the axis voice cannot resolve is supplied by Component B.

## 4. The cross-modal contract with Component B

D consumes B's ordinal stress prediction; it does no HRV maths of its own. The
level vocabularies already align:

| Component B (`CLASS_NAMES`) | Component D (`STRESS_LEVELS`) |
|---|---|
| `relaxed` | `no` (D aliases `relaxed → no`) |
| `mild` / `moderate` / `high` | identical |

B's `mode="point"` → a single level; `mode="band"` → `level_low/level_high`,
which maps onto D's band input (D takes the higher level + low confidence). B's
`confidence` feeds D's Layer-4 gate. Integration direction: **D polls B's
`GET /stress/latest`** at each check-in (B is a continuous stream; D samples it
at the two episodic moments). See `docs/DEPLOYMENT.md`.

## 5. Honest limitations

- **Sinhala is limited** — the frozen encoder is out-of-distribution for it.
  Adding collected Sinhala data *hurt* held-out Sinhala (Phase 2, negative
  result); a graded-vs-binary label ablation ruled out labeling. More data can't
  teach a frozen OOD encoder.
- **Arousal is unreliable for magnitude by design** — offloaded to Component B.
- **A confident single-speaker misread exists** (Phase 3): an OOD individual
  voice can read confidently-negative, which bypasses the defer-to-HRV gate.
- **Layer 5 trains on simulated sessions** until real longitudinal data exists.

These are disclosed, not hidden — the contribution is the *diagnosis* plus the
scoring and multimodal design that stay honest under the encoder's ceiling.
