"""Parser for real collected voice data (induced stress, our own recordings).

Kept SEPARATE from data/real_voice_eval, which stays a held-out test set.
Filename convention carries the label:
    <person>_<condition>_<number>.wav   e.g. p1_stressed_01.wav
    condition = "calm" or "stressed"

Usage:
  python -m src.datasets.collected --root data/raw/real_collected \
      --out data/metadata_collected.csv
"""

import argparse
import sys
from pathlib import Path

import pandas as pd

sys.path.insert(0, str(Path(__file__).parent.parent.parent))
from componentd.config import EMOTION_VA, stress_from_va

# Map the two collection conditions onto the canonical emotion space.
# "stressed" -> fear-like (negative valence, high arousal);
# "calm" -> calm (positive valence, low arousal). These give the model
# clean valence/arousal targets consistent with every other dataset.
CONDITION_EMOTION = {"stressed": "fear", "calm": "calm"}
CONDITION_STRESS_LABEL = {"stressed": 1, "calm": 0}


def parse_filename(path: Path):
    """<person>_<condition>_<number>.wav -> (speaker, condition), else None."""
    parts = path.stem.lower().split("_")
    if len(parts) < 3:
        return None
    person, condition = parts[0], parts[1]
    if condition not in CONDITION_EMOTION:
        return None
    return f"collected_{person}", condition


def build_metadata(root: Path, test_speaker_ratio: float = 0.0) -> pd.DataFrame:
    """Build training metadata. By default ALL collected data goes to
    train (test_speaker_ratio=0), because the real held-out test set is
    data/real_voice_eval. Pass a ratio > 0 only if you deliberately want
    a speaker-independent split within the collected data itself."""
    rows = []
    for wav in sorted(root.rglob("*.wav")):
        parsed = parse_filename(wav)
        if parsed is None:
            continue
        speaker, condition = parsed
        emotion = CONDITION_EMOTION[condition]
        va = EMOTION_VA[emotion]
        rows.append({
            "path": str(wav),
            "dataset": "collected",
            "language": "en",
            "speaker": speaker,
            "emotion": emotion,
            "valence": va["valence"],
            "arousal": va["arousal"],
            "stress01": stress_from_va(va["valence"], va["arousal"]),
            "stress_label": CONDITION_STRESS_LABEL[condition],
        })
    df = pd.DataFrame(rows)
    if df.empty:
        return df

    df["split"] = "train"
    if test_speaker_ratio > 0:
        speakers = sorted(df["speaker"].unique())
        n_test = max(1, int(len(speakers) * test_speaker_ratio))
        test_speakers = set(speakers[-n_test:])
        df.loc[df.speaker.isin(test_speakers), "split"] = "test"
    return df


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--test-speaker-ratio", type=float, default=0.0)
    args = ap.parse_args()

    meta = build_metadata(Path(args.root), args.test_speaker_ratio)
    meta.to_csv(args.out, index=False)
    print(f"wrote {len(meta)} rows -> {args.out}")
    if len(meta):
        print(meta.groupby(["split", "emotion"]).size())
        print(f"speakers: {sorted(meta['speaker'].unique())}")
    else:
        print("no valid clips found - check filenames match "
              "<person>_<condition>_<number>.wav")