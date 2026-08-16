"""Build IEMOCAP metadata + wavs from the HuggingFace mirror (AbstractTTS/IEMOCAP).

Why this file exists: this mirror ships IEMOCAP's REAL dimensional annotations
- EmoVal (valence), EmoAct (activation = arousal), EmoDom (dominance), each on
a 1-5 scale of genuine human ratings. That is the whole reason we want IEMOCAP:
continuous valence/arousal learned from real ratings instead of the per-emotion
lookup table (config.EMOTION_VA) that caused the arousal collapse on real voices.

We rescale 1-5 -> [-1, 1] via (x-3)/2 (same as src/datasets/iemocap.py), save
each utterance to a wav, and emit the project's unified metadata columns, so
extract_features.py + train_fusion.py consume it with NO changes.

Citation / ethics: cite Busso et al. (2008), "IEMOCAP", in the report. This is
an unofficial mirror - keep the official USC SAIL request in flight for a clean
citation trail. Non-commercial academic use only.

Usage (best run on Colab - the dataset is large):
  pip install -q datasets soundfile
  python scripts/build_iemocap_hf.py \
      --out-wav data/wav/iemocap --out data/metadata_iemocap.csv
"""

import argparse
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import soundfile as sf

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.config import CALM_EMOTIONS, STRESSED_EMOTIONS, stress_from_va

# HF `major_emotion` -> our canonical names. Used ONLY for the binary stress
# metric; the continuous valence/arousal come from the real EmoVal/EmoAct.
EMO_MAP = {
    "neutral": "neutral", "happy": "happiness", "sad": "sadness",
    "angry": "anger", "fear": "fear", "surprise": "surprise",
    "disgust": "disgust", "excited": "joy", "frustrated": "frustration",
}


def rescale(x) -> float:
    """IEMOCAP 1-5 rating -> [-1, 1]. 3 (neutral) -> 0."""
    return max(-1.0, min(1.0, (float(x) - 3.0) / 2.0))


def binary_stress_label(emotion: str) -> int:
    if emotion in STRESSED_EMOTIONS:
        return 1
    if emotion in CALM_EMOTIONS:
        return 0
    return -1


def speaker_of(file_name: str, gender: str, idx: int) -> str:
    """IEMOCAP turn names look like Ses01F_impro01_F000 -> a stable per-actor
    id (session + the turn-final speaker gender). Falls back to gender if the
    filename is not in that format (keeps the speaker-independent split working
    at a coarser granularity)."""
    stem = Path(str(file_name)).stem
    if stem[:3] == "Ses" and "_" in stem:
        session = stem[:5]                       # Ses01
        spk_g = stem.split("_")[-1][0]           # 'F' or 'M' of this turn
        return f"iemocap_{session}_{spk_g}"
    return f"iemocap_{str(gender).lower()[:1] or 'x'}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-wav", default="data/wav/iemocap")
    ap.add_argument("--out", default="data/metadata_iemocap.csv")
    ap.add_argument("--test-speaker-ratio", type=float, default=0.2)
    ap.add_argument("--hf-name", default="AbstractTTS/IEMOCAP")
    args = ap.parse_args()

    from datasets import load_dataset
    ds = load_dataset(args.hf_name)
    # single split ("train") or a DatasetDict - take the first split either way
    split = ds[next(iter(ds.keys()))] if hasattr(ds, "keys") else ds
    print(f"loaded {len(split)} utterances from {args.hf_name}")

    wav_dir = Path(args.out_wav)
    wav_dir.mkdir(parents=True, exist_ok=True)

    rows = []
    for i, ex in enumerate(split):
        audio = ex["audio"]
        arr = np.asarray(audio["array"], dtype=np.float32)
        sr = int(audio["sampling_rate"])
        wav_path = wav_dir / f"iemocap_{i:05d}.wav"
        sf.write(str(wav_path), arr, sr)

        emotion = EMO_MAP.get(str(ex.get("major_emotion", "")).lower(), "unknown")
        valence, arousal = rescale(ex["EmoVal"]), rescale(ex["EmoAct"])
        rows.append({
            "path": str(wav_path),
            "dataset": "iemocap",
            "language": "en",
            "speaker": speaker_of(ex.get("file", ""), ex.get("gender", ""), i),
            "emotion": emotion,
            "valence": valence,
            "arousal": arousal,
            # stress01 from the REAL V/A (a genuinely learned continuous target).
            "stress01": stress_from_va(valence, arousal),
            "stress_label": binary_stress_label(emotion),
        })

    df = pd.DataFrame(rows)
    # Speaker-independent split: whole actors to test, never mixed.
    speakers = sorted(df["speaker"].unique())
    n_test = max(1, int(len(speakers) * args.test_speaker_ratio))
    test_speakers = set(speakers[-n_test:])
    df["split"] = df["speaker"].apply(
        lambda s: "test" if s in test_speakers else "train")

    df.to_csv(args.out, index=False)
    print(f"wrote {len(df)} rows -> {args.out}")
    print(df.groupby("split").size())
    print(f"speakers: {df['speaker'].nunique()}  "
          f"arousal {df['arousal'].min():.2f}..{df['arousal'].max():.2f} "
          f"(mean {df['arousal'].mean():.2f}) - CONTINUOUS, real ratings")


if __name__ == "__main__":
    main()
