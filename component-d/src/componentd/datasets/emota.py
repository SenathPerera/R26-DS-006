"""EmoTa parser: Tamil emotional speech (Phase 2, after access approval).

936 utterances, 22 Sri Lankan Tamil speakers, 5 emotions.
Access: request form at https://rtuthaya.staff.uom.lk/contact-for-resources
Filename convention (verified against the official emota_loader):
    <spkID>_<senID>_<emo>.wav      e.g. 19_18_ang.wav
"""

import argparse
import sys
from pathlib import Path

import pandas as pd

sys.path.insert(0, str(Path(__file__).parent.parent.parent))
from componentd.config import CALM_EMOTIONS, EMOTION_VA, STRESSED_EMOTIONS, stress_from_va

# 3-letter codes in EmoTa filenames -> canonical emotion names.
EMOTION_CODES = {
    "ang": "anger", "hap": "happiness", "sad": "sadness",
    "fea": "fear", "neu": "neutral",
}


def parse_filename(path: Path) -> tuple[str, str] | None:
    """<spkID>_<senID>_<emo>.wav -> (speaker, emotion), else None."""
    parts = path.stem.split("_")
    if len(parts) != 3:
        return None
    spk_id, _, emo = parts
    emotion = EMOTION_CODES.get(emo[:3].lower())
    if emotion is None or not spk_id.isdigit():
        return None
    return f"emota_{int(spk_id)}", emotion


def binary_stress_label(emotion: str) -> int:
    if emotion in STRESSED_EMOTIONS:
        return 1
    if emotion in CALM_EMOTIONS:
        return 0
    return -1


def build_metadata(emota_root: Path, test_speaker_ratio: float = 0.2) -> pd.DataFrame:
    rows = []
    for wav in sorted(emota_root.rglob("*.wav")):
        parsed = parse_filename(wav)
        if parsed is None:
            continue
        speaker, emotion = parsed
        va = EMOTION_VA[emotion]
        rows.append({
            "path": str(wav), "dataset": "emota", "language": "ta",
            "speaker": speaker, "emotion": emotion,
            "valence": va["valence"], "arousal": va["arousal"],
            "stress01": stress_from_va(va["valence"], va["arousal"]),
            "stress_label": binary_stress_label(emotion),
        })
    df = pd.DataFrame(rows)
    if df.empty:
        return df

    # Speaker-independent split: whole speakers go to test, never mixed.
    speakers = sorted(df["speaker"].unique())
    n_test = max(1, int(len(speakers) * test_speaker_ratio))
    test_speakers = set(speakers[-n_test:])
    df["split"] = df["speaker"].apply(
        lambda s: "test" if s in test_speakers else "train")
    return df


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--emota-root", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    meta = build_metadata(Path(args.emota_root))
    meta.to_csv(args.out, index=False)
    print(f"wrote {len(meta)} rows -> {args.out}")
    if len(meta):
        print(meta.groupby(["split", "emotion"]).size())
        print(f"speakers: {meta['speaker'].nunique()}")
