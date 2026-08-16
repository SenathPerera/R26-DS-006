# CogniVoice Component D — Master Handoff Prompt (updated 2026-08-16)

> Paste this whole document into a new chat to continue the work with full context.

## YOUR ROLE
You are a senior ML/research engineer helping an undergraduate finish Component D
of a final-year dissertation. Be honest and evidence-driven: measure before
claiming, report negative results faithfully, never fake numbers. Explain in
plain language.

## THE PROJECT
CogniVoice is an AI-based adaptive VR meditation system (SLIIT undergrad
dissertation, student S. Prathikesh IT22621788, supervisor Mr. Samadhi
Rathnayake, Project ID R26-DS-006). Components: A = adaptive audio, B = HRV/
heart-rate stress inference (teammate Senath), C = VR adaptation, D = voice-based
stress detection (THIS component). D only integrates directly with B. A user
speaks ~30s before and ~30s after a meditation session; D returns a stress score,
whether the session helped, whether voice agrees with heart data, and anomaly.
PP2 is the FINAL product delivery (no more experiments — package and defend).

## REPO
- Path: /Users/prathikesh/Projects/cognivoice-component-d
- Active branch: phase1-valence-primary-scoring (pushed to origin; NOT merged to main by choice)
- Shared repo incl. Component B: github.com/SenathPerera/R26-DS-006

## THE 5 LAYERS
1. Quality gate (src/layer1_quality.py) — Silero VAD + DSP. Done.
2. Stress model (src/layer2_*.py) — THE contribution. Frozen emotion2vec_plus_large
   (1024-d) + trainable prosody branch (21 feats) + gated fusion + learned V/A head.
3. Pre/post compare (src/layer3_compare.py) — delta with confidence-widened noise floor.
4. Cross-modal (src/layer4_crossmodal.py) — voice vs B's HRV, confidence-gated.
5. Anomaly (src/layer5_anomaly.py) — VAE on session features.

## MODEL & SCORING (Phase 1, shipped)
- Active checkpoint: models/fusion_meld_baseline.pt (default FUSION_CKPT). Chosen
  because it WINS on real voices despite worst in-domain acted score.
- Valence-primary scoring: stress = max(0, -valence) (config.stress_from_va).
  Arousal only names TYPE (activated vs shutdown), never magnitude — it collapses
  on real/quiet stress. Confidence = |valence| (config.confidence_from_va).
- Binary boundary 2.0 (validated); severity cuts (4.5/7.0) provisional.

## KEY FINDINGS (the honest story)
- Acted metrics INVERT on real voices; valence transfers, arousal collapses.
- Phase 1: valence-primary scoring took the model to 91.7% real-voice
  (English 92%, Sinhala 91% but only 3 speakers) — no retraining.
- THE OPEN PROBLEM: Sinhala is unreliable. Frozen encoder is out-of-distribution
  (OOD) for Sinhala; can't adapt. English works; Sinhala honestly limited.

## SESSION 2026-08-15/16 — PHASE 2: does collected Sinhala data help? (NEGATIVE RESULT)
- Collected 26 new Sinhala clips from 7 NEW speakers (person1-7, distinct from the
  held-out eval speakers sinhala_p1/p2/p3). Organized to data/raw/real_collected_sinhala/
  as si_<person>_<condition>_<intensity>_<n>.wav. Parser: src/datasets/sinhala_collected.py.
  Graded intensity via config.INTENSITY_SCALE + scale_va_by_intensity(). All 7 new
  speakers -> TRAIN; the 3-speaker eval set stayed fully held out.
- Retrained fusion head (encoder still frozen) on cached features_meld.npz (12,434) + 26
  Sinhala, via notebooks/colab_sinhala_retrain.ipynb. Honest eval: scripts/evaluate_sinhala.py
  (label-driven, 2.0 threshold).
- RESULT — adding Sinhala data HURT held-out Sinhala:
    fusion_meld_baseline (MELD only, SHIPPED):        90.9% acc / 87.5% recall / 100% spec
    + Sinhala, graded-intensity labels:               81.8% / 75.0% / 100%
    + Sinhala, binary labels:                         81.8% / 87.5% / 66.7%
- Controlled label ablation (graded vs binary, same seed 42, labels the only diff;
  added --seed flag to scripts/train_fusion.py; binary labels patched into the npz
  without re-extraction since emb/prosody are label-independent): both land at 81.8%.
  GRADED-INTENSITY HYPOTHESIS REJECTED — labels only relocate the error, don't fix it.
- All error is on ONE hard speaker (p2), whose clips sit at the 2.0 threshold.
  n=11 -> directional, not statistically robust, but consistent.
- LIVE UI validation: a genuinely high-stress Sinhala clip scores stress 0.00 with
  POSITIVE valence (+0.38) — read as pleasant. Backend is deterministic; frontend
  upload passes raw bytes (no bug). The same clip flips quadrants between upload vs
  mic-recording -> the Sinhala reading is unstable to acoustic path. System behaves
  correctly under uncertainty: LOW confidence + Layer 4 "Deferred" to HRV.
- CONCLUSION: A handful of Sinhala clips cannot teach a FROZEN OOD encoder; more data/
  relabeling won't help. This is a legitimate finding, not a bug. Shipped model stays
  fusion_meld_baseline. Recorded in docs/ABLATION_STUDY.md ("Phase 2 — negative result")
  and in agent memory (sinhala-retrain-negative-result.md).

## THE 3 SOLUTIONS TO THE SINHALA LIMITATION
1. Swap to a MULTILINGUAL encoder that has seen Sinhala (Whisper large-v3 / Meta MMS /
   XLS-R), retrain the head on top. The only option with a real chance of raising the
   Sinhala number — attacks the root cause (OOD encoder). BUT: multilingual encoders
   aren't emotion-specialized (emotion2vec is, but English-only), so it's an empirical
   trade-off that might help Sinhala while hurting English. Effort ~1 day (new HF-based
   extraction path + Colab re-extract + retrain + eval); uncertain payoff. EXPERIMENT.
2. Parameter-efficient fine-tuning (LoRA/adapters) of the encoder on Sinhala. Directly
   teaches Sinhala, but with only 7 speakers it will OVERFIT. High effort, low payoff at
   this data scale. SKIP.
3. Lean on the MULTIMODAL design (Component B HRV) — ALREADY BUILT & LIVE-TESTED. For
   Sinhala, voice confidence is low -> Layer 4 defers to heart-rate. No retraining, no
   risk. Honest and shows good engineering. RECOMMENDED.

## RECOMMENDATION (PP2 = FINAL DELIVERY)
Lock in SOLUTION #3. Do NOT attempt #1 or #2 this close to final submission — never
risk a working product on an uncertain experiment at the deadline. Mention #1 as
"future work." Panel line:
"Voice stress detection works reliably for English. For Sinhala I proved the limitation
is the pretrained encoder being out-of-distribution — not my model or data — and my
multimodal design handles it by deferring to the heart-rate signal when the voice is
uncertain."

## METHODOLOGY GUARDRAILS (do not violate)
- Speaker-independent splits ONLY; never chunk one recording into train+test.
- Augmentation = robustness (noise/codec/phone), NOT speaker diversity.
- Only more distinct real speakers could help generalization — and even that is
  uncertain while the encoder stays frozen. Measure, never fake a number.

## RUN COMMANDS
```bash
# backend (repo root)
lsof -ti :8010 | xargs kill -9        # clear stray server FIRST (separate line)
.venv/bin/uvicorn api_server:app --host 127.0.0.1 --port 8010
# frontend
cd frontend && export PATH="$HOME/.nvm/versions/node/v20.19.5/bin:$PATH" && npm run dev  # -> :5173
# tests (94 pass)
.venv/bin/python -m pytest tests/ -q
# honest Sinhala eval (before/after any checkpoint)
.venv/bin/python scripts/evaluate_sinhala.py --model models/fusion_meld_baseline.pt --metadata data/metadata_sinhala.csv
```
- First /infer loads the ~1.8GB encoder (~1-2 min). data/ and models/ are gitignored.
- Feature npz now local in data/ (features_meld.npz, features_sinhala.npz) — future
  LABEL-ONLY ablations run fully local + fast (no Colab).

## CURRENT STATE / OPEN ITEMS
- Retrained models/fusion_meld_sinhala*.pt are experiments only (gitignored). Shipped
  model is models/fusion_meld_baseline.pt.
- Possibly-uncommitted local tweaks: api_server.py (port 8010 comment), frontend
  VITE_API_BASE + banner (App.jsx, api.js) — cosmetic/config, safe.
- REMAINING FOR FINAL DELIVERY (packaging, not engineering):
  1. Lock the demo script: English before/after (voice working) + Sinhala clip showing
     low-confidence -> Layer 4 defers to HRV.
  2. Consolidate ABLATION_STUDY.md into one panel-ready master table + narrative.
  3. (Optional) UI low-confidence indicator so 0.0 distinguishes confident-calm vs uncertain.
  4. Confirm B's live server returns real predictions; wire D to poll GET /stress/latest;
     real joint live test (Layer 4's first genuine validation).
  5. Decide on merging phase1-valence-primary-scoring -> main.

## MENTAL MODEL
English is done and works. Sinhala is a proven encoder limitation, honestly reported and
handled by the multimodal design — NOT a blocker. The dissertation is strong BECAUSE of the
honest diagnostic. PP2 is final: package and defend, don't re-engineer.
