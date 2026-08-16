"""MELD parser: naturalistic conversational English (the main training set).

MELD ships as label CSVs + video clips; audio must be extracted to wav
first (ffmpeg). This parser walks the label CSVs, keeps rows whose wav
exists, and emits the project-wide unified metadata format:

    path, dataset, language, split, speaker, emotion,
    valence, arousal, stress01, stress_label

Usage:
  python -m src.datasets.meld --meld-root data/raw/MELD.Raw \
      --wav-dir data/wav/meld --out data/metadata_meld.csv
"""

import argparse
import subprocess
import sys
from pathlib import Path

import pandas as pd

sys.path.insert(0, str(Path(__file__).parent.parent.parent))
from componentd.config import CALM_EMOTIONS, EMOTION_VA, STRESSED_EMOTIONS, stress_from_va

# MELD's emotion names -> our canonical names used in EMOTION_VA.
MELD_EMOTION_MAP = {
    "neutral": "neutral", "joy": "joy", "surprise": "surprise",
    "anger": "anger", "fear": "fear", "sadness": "sadness",
    "disgust": "disgust",
}

# (label csv, clip folder) per split, exactly as shipped in MELD.Raw.
SPLITS = {
    "train": ("train_sent_emo.csv", "train_splits"),
    "val": ("dev_sent_emo.csv", "dev_splits_complete"),
    "test": ("test_sent_emo.csv", "output_repeated_splits_test"),
}


def binary_stress_label(emotion: str) -> int:
    """1 = stressed, 0 = calm, -1 = ambiguous (excluded from binary
    metrics; still used for valence/arousal regression)."""
    if emotion in STRESSED_EMOTIONS:
        return 1
    if emotion in CALM_EMOTIONS:
        return 0
    return -1


def extract_audio(mp4_path: Path, wav_path: Path) -> bool:
    """One clip: mp4 -> 16 kHz mono wav via ffmpeg."""
    wav_path.parent.mkdir(parents=True, exist_ok=True)
    cmd = ["ffmpeg", "-y", "-i", str(mp4_path),
           "-ar", "16000", "-ac", "1", "-loglevel", "error", str(wav_path)]
    return subprocess.run(cmd).returncode == 0


def build_metadata(meld_root: Path, wav_dir: Path,
                   convert: bool = False) -> pd.DataFrame:
    rows = []
    for split, (csv_name, clip_dir) in SPLITS.items():
        csv_path = meld_root / csv_name
        if not csv_path.exists():
            print(f"warning: {csv_path} missing, skipping split '{split}'")
            continue
        for _, r in pd.read_csv(csv_path).iterrows():
            emotion = MELD_EMOTION_MAP.get(str(r["Emotion"]).lower())
            if emotion is None:
                continue
            clip = f"dia{r['Dialogue_ID']}_utt{r['Utterance_ID']}"
            wav = wav_dir / split / f"{clip}.wav"

            if convert and not wav.exists():
                mp4 = meld_root / clip_dir / f"{clip}.mp4"
                if mp4.exists():
                    extract_audio(mp4, wav)
            if not wav.exists():
                continue  # only index audio that really exists on disk

            va = EMOTION_VA[emotion]
            rows.append({
                "path": str(wav),
                "dataset": "meld",
                "language": "en",
                "split": split,
                "speaker": f"meld_{r.get('Speaker', 'unknown')}",
                "emotion": emotion,
                "valence": va["valence"],
                "arousal": va["arousal"],
                "stress01": stress_from_va(va["valence"], va["arousal"]),
                "stress_label": binary_stress_label(emotion),
            })
    return pd.DataFrame(rows)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--meld-root", required=True)
    ap.add_argument("--wav-dir", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--convert", action="store_true",
                    help="also run ffmpeg mp4->wav for missing wavs")
    args = ap.parse_args()

    meta = build_metadata(Path(args.meld_root), Path(args.wav_dir), args.convert)
    meta.to_csv(args.out, index=False)
    print(f"wrote {len(meta)} rows -> {args.out}")
    if len(meta):
        print(meta.groupby(["split", "emotion"]).size())
