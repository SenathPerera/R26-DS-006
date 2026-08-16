"""Honest held-out evaluation on the Sinhala eval speakers.

Unlike evaluate_real_voice.py (filename heuristics + a legacy 0-10 cutoff of
5), this reads the GROUND-TRUTH labels from a metadata CSV and applies the
SHIPPED binary boundary (config.STRESSED_THRESHOLD = 2.0). It is label-driven
so the number it reports is the number the product would actually get.

Designed for a fair before/after comparison: point --model at the OLD checkpoint
(MELD only) and the NEW one (MELD + Sinhala) over the SAME held-out speakers.
Those speakers (sinhala_p1/p2/p3) are never in training for either model, so
this is a clean speaker-independent test - no LOO, no fit-to-test bias.

Usage:
  .venv/bin/python scripts/evaluate_sinhala.py \
      --model models/fusion_meld_baseline.pt \
      --metadata data/metadata_sinhala.csv

  # before/after in one go:
  .venv/bin/python scripts/evaluate_sinhala.py \
      --model models/fusion_meld_baseline.pt models/fusion_meld_sinhala.pt \
      --metadata data/metadata_sinhala.csv
"""

import argparse
import sys
from pathlib import Path

import pandas as pd

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.config import STRESSED_THRESHOLD
from componentd.layer2_inference import StressScorer


def evaluate_model(model_path: str, meta: pd.DataFrame) -> dict:
    """Score every clip in `meta` with one checkpoint; return metrics + rows."""
    scorer = StressScorer(model_path)
    rows = []
    for _, r in meta.iterrows():
        rep = scorer.score_file(r["path"])
        pred = 1 if rep["stress_score"] >= STRESSED_THRESHOLD else 0
        rows.append({
            "path": Path(r["path"]).name,
            "speaker": r["speaker"],
            "true": int(r["stress_label"]),
            "pred": pred,
            "score": rep["stress_score"],
            "conf": rep["confidence"],
            "valence": rep["valence"],
            "arousal": rep["arousal"],
            "correct": pred == int(r["stress_label"]),
        })
    df = pd.DataFrame(rows)

    tp = int(((df.true == 1) & (df.pred == 1)).sum())
    fn = int(((df.true == 1) & (df.pred == 0)).sum())
    tn = int(((df.true == 0) & (df.pred == 0)).sum())
    fp = int(((df.true == 0) & (df.pred == 1)).sum())
    n = len(df)
    metrics = {
        "n": n,
        "accuracy": (tp + tn) / n if n else 0.0,
        "stressed_recall": tp / (tp + fn) if (tp + fn) else float("nan"),
        "calm_specificity": tn / (tn + fp) if (tn + fp) else float("nan"),
        "tp": tp, "fn": fn, "tn": tn, "fp": fp,
    }
    return {"metrics": metrics, "rows": df}


def print_report(model_path: str, res: dict):
    m = res["metrics"]
    df = res["rows"]
    print(f"\n{'='*78}\nMODEL: {model_path}")
    print(f"{'file':<34}{'spk':<16}{'true':>5}{'pred':>5}{'score':>7}{'conf':>6}  ok")
    print("-" * 78)
    for _, r in df.iterrows():
        mark = "OK" if r.correct else "XX"
        print(f"{r.path:<34}{r.speaker:<16}{r.true:>5}{r.pred:>5}"
              f"{r.score:>7.2f}{r.conf:>6.2f}  {mark}")
    print("-" * 78)
    print(f"n={m['n']}  accuracy={m['accuracy']*100:.1f}%  "
          f"stressed_recall={m['stressed_recall']*100:.1f}%  "
          f"calm_specificity={m['calm_specificity']*100:.1f}%")
    print(f"confusion: TP={m['tp']} FN={m['fn']} TN={m['tn']} FP={m['fp']}  "
          f"(threshold={STRESSED_THRESHOLD})")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--model", nargs="+", required=True,
                    help="one or more checkpoints (for before/after comparison)")
    ap.add_argument("--metadata", default="data/metadata_sinhala.csv",
                    help="CSV with path + stress_label + speaker columns")
    args = ap.parse_args()

    meta = pd.read_csv(args.metadata)
    missing = [p for p in meta["path"] if not Path(p).exists()]
    if missing:
        print(f"WARNING: {len(missing)} clip paths do not exist, e.g. {missing[0]}")
        meta = meta[meta["path"].apply(lambda p: Path(p).exists())].reset_index(drop=True)

    print(f"Evaluating on {len(meta)} clips from {args.metadata} "
          f"({meta['speaker'].nunique()} speakers)")

    summary = []
    for mp in args.model:
        if not Path(mp).exists():
            print(f"skip missing checkpoint: {mp}")
            continue
        res = evaluate_model(mp, meta)
        print_report(mp, res)
        summary.append((mp, res["metrics"]))

    if len(summary) > 1:
        print(f"\n{'='*78}\nBEFORE / AFTER SUMMARY")
        print(f"{'model':<44}{'acc':>7}{'recall':>8}{'spec':>7}")
        print("-" * 78)
        for mp, m in summary:
            print(f"{Path(mp).name:<44}{m['accuracy']*100:>6.1f}%"
                  f"{m['stressed_recall']*100:>7.1f}%{m['calm_specificity']*100:>6.1f}%")


if __name__ == "__main__":
    main()
