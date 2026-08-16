"""Parser for the newly collected Sinhala TRAINING data (2026-08).

Kept SEPARATE from data/real_voice_eval (the held-out Sinhala test set,
sinhala_p1/p2/p3, metadata_sinhala.csv) — this is the 7-speaker batch
(person1..person7) collected to give Sinhala a real training signal for
the first time. Filename convention carries the label:
    si_<person>_<condition>_<intensity>_<number>.wav
    condition = calm | stress | happy | fear | sad | neutral
    intensity = low | mild | high (self-reported by the speaker)

Usage:
  python -m src.datasets.sinhala_collected \
      --root data/raw/real_collected_sinhala \
      --out data/metadata_sinhala_collected.csv
"""

import argparse
import sys
from pathlib import Path

import pandas as pd

sys.path.insert(0, str(Path(__file__).parent.parent.parent))
from componentd.config import scale_va_by_intensity, stress_from_va

# Map each collected condition onto the canonical EMOTION_VA anchor it
# scales from, and onto the binary stress label (per PP2 decision:
# happy/neutral -> calm side, fear/sad -> stressed side, matching the
# valence-primary convention already used everywhere else).
CONDITION_EMOTION = {
    "calm": "calm",
    "stress": "fear",
    "fear": "fear",
    "happy": "joy",
    "sad": "sadness",
    "neutral": "neutral",
}
CONDITION_STRESS_LABEL = {
    "calm": 0, "happy": 0, "neutral": 0,
    "stress": 1, "fear": 1, "sad": 1,
}


def parse_filename(path: Path):
    """si_<person>_<condition>_<intensity>_<number>.wav -> (speaker, condition, intensity)."""
    parts = path.stem.lower().split("_")
    if len(parts) != 5 or parts[0] != "si":
        return None
    _, person, condition, intensity, _num = parts
    if condition not in CONDITION_EMOTION or intensity not in ("low", "mild", "high"):
        return None
    return f"sinhala_{person}", condition, intensity


def build_metadata(root: Path) -> pd.DataFrame:
    """Build training metadata. ALL of this batch goes to train — the real
    held-out Sinhala test set is data/real_voice_eval (sinhala_p1/p2/p3),
    a disjoint set of speakers, so no speaker-independent carve-out is
    needed here."""
    rows = []
    for wav in sorted(root.rglob("*.wav")):
        parsed = parse_filename(wav)
        if parsed is None:
            print(f"skip (unrecognized name): {wav.name}")
            continue
        speaker, condition, intensity = parsed
        emotion = CONDITION_EMOTION[condition]
        va = scale_va_by_intensity(emotion, intensity)
        rows.append({
            "path": str(wav),
            "dataset": "sinhala_collected",
            "language": "si",
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
    return df


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    df = build_metadata(Path(args.root))
    df.to_csv(args.out, index=False)
    print(f"Wrote {len(df)} rows to {args.out}")
    print(df.groupby(["speaker", "stress_label"]).size())
