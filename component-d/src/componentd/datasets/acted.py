"""Acted English datasets: RAVDESS, CREMA-D, TESS (the PP1 sets).

Role in PP2: auxiliary training data + the acted-vs-natural ablation row.
Each parser decodes emotion and speaker from the official file naming,
then all three are merged with speaker-independent splits.

Usage:
  python -m src.datasets.acted --ravdess <dir> --cremad <dir> --tess <dir> \
      --out data/metadata_acted.csv
"""

import argparse
import sys
from pathlib import Path

import pandas as pd

sys.path.insert(0, str(Path(__file__).parent.parent.parent))
from componentd.config import CALM_EMOTIONS, EMOTION_VA, STRESSED_EMOTIONS, stress_from_va

# RAVDESS filename: 03-01-05-01-01-01-12.wav
# fields: modality-channel-EMOTION-intensity-statement-repetition-ACTOR
RAVDESS_EMOTIONS = {
    "01": "neutral", "02": "calm", "03": "joy", "04": "sadness",
    "05": "anger", "06": "fear", "07": "disgust", "08": "surprise",
}

# CREMA-D filename: 1001_DFA_ANG_XX.wav (SPEAKER_sentence_EMOTION_level)
CREMAD_EMOTIONS = {
    "ANG": "anger", "DIS": "disgust", "FEA": "fear",
    "HAP": "joy", "NEU": "neutral", "SAD": "sadness",
}

# TESS: emotion appears in the folder or filename (OAF_back_angry.wav).
TESS_EMOTIONS = {
    "angry": "anger", "disgust": "disgust", "fear": "fear",
    "happy": "joy", "neutral": "neutral", "sad": "sadness",
    "ps": "surprise",  # "pleasant surprise"
}


def binary_stress_label(emotion: str) -> int:
    if emotion in STRESSED_EMOTIONS:
        return 1
    if emotion in CALM_EMOTIONS:
        return 0
    return -1


def _row(path: Path, dataset: str, speaker: str, emotion: str) -> dict:
    va = EMOTION_VA[emotion]
    return {
        "path": str(path), "dataset": dataset, "language": "en",
        "speaker": speaker, "emotion": emotion,
        "valence": va["valence"], "arousal": va["arousal"],
        "stress01": stress_from_va(va["valence"], va["arousal"]),
        "stress_label": binary_stress_label(emotion),
    }


def parse_ravdess(root: Path) -> list[dict]:
    rows = []
    for wav in root.rglob("*.wav"):
        parts = wav.stem.split("-")
        if len(parts) != 7:
            continue
        emotion = RAVDESS_EMOTIONS.get(parts[2])
        if emotion:
            rows.append(_row(wav, "ravdess", f"ravdess_{parts[6]}", emotion))
    return rows


def parse_cremad(root: Path) -> list[dict]:
    rows = []
    for wav in root.rglob("*.wav"):
        parts = wav.stem.split("_")
        if len(parts) < 3:
            continue
        emotion = CREMAD_EMOTIONS.get(parts[2])
        if emotion:
            rows.append(_row(wav, "cremad", f"cremad_{parts[0]}", emotion))
    return rows


def parse_tess(root: Path) -> list[dict]:
    rows = []
    for wav in root.rglob("*.wav"):
        # emotion is the last token of the filename: OAF_back_angry.wav
        token = wav.stem.split("_")[-1].lower()
        emotion = TESS_EMOTIONS.get(token)
        # speaker is the first token: OAF (older) / YAF (younger)
        speaker = wav.stem.split("_")[0].upper()
        if emotion:
            rows.append(_row(wav, "tess", f"tess_{speaker}", emotion))
    return rows


def assign_splits(df: pd.DataFrame, test_ratio: float = 0.2) -> pd.DataFrame:
    """Speaker-independent splits: a speaker's clips are ALL train or ALL
    test, never both - otherwise the model memorises voices and the test
    score lies. TESS (2 speakers) goes entirely to train for this reason."""
    df["split"] = "train"
    for dataset in ["ravdess", "cremad"]:
        speakers = sorted(df.loc[df.dataset == dataset, "speaker"].unique())
        n_test = max(1, int(len(speakers) * test_ratio))
        test_speakers = set(speakers[-n_test:])
        mask = (df.dataset == dataset) & df.speaker.isin(test_speakers)
        df.loc[mask, "split"] = "test"
    return df


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--ravdess")
    ap.add_argument("--cremad")
    ap.add_argument("--tess")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    rows = []
    if args.ravdess:
        rows += parse_ravdess(Path(args.ravdess))
    if args.cremad:
        rows += parse_cremad(Path(args.cremad))
    if args.tess:
        rows += parse_tess(Path(args.tess))

    df = assign_splits(pd.DataFrame(rows))
    df.to_csv(args.out, index=False)
    print(f"wrote {len(df)} rows -> {args.out}")
    if len(df):
        print(df.groupby(["dataset", "split"]).size())
