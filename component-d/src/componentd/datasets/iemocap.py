"""IEMOCAP parser: the fix for the arousal-collapse problem.

Unlike MELD/acted/emota, IEMOCAP ships REAL human-annotated valence and
arousal for every utterance (evaluators rated each on a 1-5 scale). We
use those genuine continuous numbers as the model's regression targets
instead of the per-emotion lookup table in config.EMOTION_VA - which is
the whole point: the lookup table taught the model "arousal = loud acted
anger", so it collapsed arousal on real, quietly-tense voices. Real V/A
labels are what teach the model true activation.

The rest of the pipeline is UNCHANGED: this emits the same unified
metadata columns (path, dataset, language, split, speaker, emotion,
valence, arousal, stress01, stress_label) every other parser produces, so
extract_features.py and train_fusion.py consume it with no edits.

IEMOCAP layout this parser expects (the official IEMOCAP_full_release):
    Session{1..5}/
        dialog/EmoEvaluation/*.txt      <- labels + [V, A, D] annotations
        sentences/wav/<dialog>/<turn>.wav

Each EmoEvaluation summary line looks like:
    [6.2901 - 8.2357]\tSes01F_impro01_F000\tneu\t[2.5000, 2.5000, 2.5000]
                                                    valence  arousal  dominance
(the three bracketed numbers are valence, activation/arousal, dominance,
each 1-5; we rescale to [-1, 1] with (x - 3) / 2).

Usage:
  python -m src.datasets.iemocap --iemocap-root data/raw/IEMOCAP_full_release \
      --out data/metadata_iemocap.csv
"""

import argparse
import re
import sys
from pathlib import Path

import pandas as pd

sys.path.insert(0, str(Path(__file__).parent.parent.parent))
from componentd.config import CALM_EMOTIONS, STRESSED_EMOTIONS, stress_from_va

# IEMOCAP 3-letter categorical codes -> our canonical emotion names.
# Only used for the BINARY stress metric (stress_label); the continuous
# valence/arousal come from the real annotations, not from these.
EMOTION_CODES = {
    "ang": "anger", "hap": "happiness", "sad": "sadness",
    "neu": "neutral", "fea": "fear", "sur": "surprise",
    "dis": "disgust", "exc": "joy",       # "excited" -> joy-like
    "fru": "frustration",                 # no calm/stressed reading -> ambiguous
    "oth": "other", "xxx": "unknown",     # no rater agreement
}

# Summary line: [start - end] TAB turn TAB emo TAB [val, act, dom]
_LINE = re.compile(
    r"^\[[\d.]+ - [\d.]+\]\s+(\S+)\s+(\w+)\s+\[([\d.]+), ([\d.]+), ([\d.]+)\]"
)


def binary_stress_label(emotion: str) -> int:
    """1 = stressed, 0 = calm, -1 = ambiguous (excluded from the binary
    metric; the clip still trains valence/arousal regression)."""
    if emotion in STRESSED_EMOTIONS:
        return 1
    if emotion in CALM_EMOTIONS:
        return 0
    return -1


def _rescale(x: float) -> float:
    """IEMOCAP 1-5 rating -> [-1, 1]. 3 (neutral) -> 0."""
    return max(-1.0, min(1.0, (x - 3.0) / 2.0))


def speaker_of(turn: str) -> str:
    """Turn name Ses01F_impro01_F000 -> a stable per-actor speaker id.
    The leading 'Ses01F' fixes the session actor; the turn-final F/M is the
    speaker WITHIN the dyad. Two actors per session -> 10 speakers total,
    which is what the speaker-independent split below relies on."""
    session = turn[:5]                       # Ses01
    spk_gender = turn.split("_")[-1][0]      # 'F' or 'M' of this turn
    return f"iemocap_{session}_{spk_gender}"


def parse_eval_file(txt_path: Path) -> list[dict]:
    rows = []
    for line in txt_path.read_text(errors="ignore").splitlines():
        m = _LINE.match(line)
        if not m:
            continue
        turn, code, v, a, _d = m.groups()
        emotion = EMOTION_CODES.get(code, "unknown")
        valence, arousal = _rescale(float(v)), _rescale(float(a))
        rows.append({
            "turn": turn,
            "emotion": emotion,
            "valence": valence,
            "arousal": arousal,
            # stress01 derived from the REAL V/A, not a lookup - a genuinely
            # learned continuous target is the whole reason we switched here.
            "stress01": stress_from_va(valence, arousal),
            "stress_label": binary_stress_label(emotion),
            "speaker": speaker_of(turn),
        })
    return rows


def wav_for_turn(iemocap_root: Path, session: str, turn: str) -> Path | None:
    """Ses01F_impro01_F000 -> Session1/sentences/wav/Ses01F_impro01/<turn>.wav"""
    dialog = "_".join(turn.split("_")[:-1])   # Ses01F_impro01
    wav = (iemocap_root / session / "sentences" / "wav" / dialog / f"{turn}.wav")
    return wav if wav.exists() else None


def build_metadata(iemocap_root: Path,
                   test_speaker_ratio: float = 0.2) -> pd.DataFrame:
    rows = []
    for s in range(1, 6):
        session = f"Session{s}"
        eval_dir = iemocap_root / session / "dialog" / "EmoEvaluation"
        if not eval_dir.exists():
            print(f"warning: {eval_dir} missing, skipping {session}")
            continue
        for txt in sorted(eval_dir.glob("*.txt")):
            for r in parse_eval_file(txt):
                wav = wav_for_turn(iemocap_root, session, r["turn"])
                if wav is None:
                    continue   # only index audio that exists on disk
                rows.append({
                    "path": str(wav),
                    "dataset": "iemocap",
                    "language": "en",
                    "speaker": r["speaker"],
                    "emotion": r["emotion"],
                    "valence": r["valence"],
                    "arousal": r["arousal"],
                    "stress01": r["stress01"],
                    "stress_label": r["stress_label"],
                })
    df = pd.DataFrame(rows)
    if df.empty:
        return df

    # Speaker-independent split: whole actors go to test, never mixed - so
    # the score reflects generalisation to unseen voices, not memorisation.
    speakers = sorted(df["speaker"].unique())
    n_test = max(1, int(len(speakers) * test_speaker_ratio))
    test_speakers = set(speakers[-n_test:])
    df["split"] = df["speaker"].apply(
        lambda sp: "test" if sp in test_speakers else "train")
    return df


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--iemocap-root", required=True,
                    help="path to IEMOCAP_full_release")
    ap.add_argument("--out", required=True)
    ap.add_argument("--test-speaker-ratio", type=float, default=0.2)
    args = ap.parse_args()

    meta = build_metadata(Path(args.iemocap_root), args.test_speaker_ratio)
    meta.to_csv(args.out, index=False)
    print(f"wrote {len(meta)} rows -> {args.out}")
    if len(meta):
        print(meta.groupby(["split", "emotion"]).size())
        print(f"speakers: {meta['speaker'].nunique()}  "
              f"({sorted(meta['speaker'].unique())})")
        print(f"arousal range: {meta['arousal'].min():.2f} "
              f"to {meta['arousal'].max():.2f} "
              f"(mean {meta['arousal'].mean():.2f}) "
              f"- CONTINUOUS, not lookup values")
