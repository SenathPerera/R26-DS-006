"""The acid test: score real (non-MELD) voice recordings.

This is the test that exposed PP1's earlier models - high accuracy on
acted training data but failure on genuine spontaneous/TTS voices.
A trained model is only useful if it also works here, not just on the
dataset it was trained on.

Usage:
  .venv/bin/python scripts/evaluate_real_voice.py <folder_or_files...>

Files with "calm"/"relaxed" in the name are treated as expected-calm,
"stress" in the name as expected-stressed, for a rough sanity check.
Everything else is scored with no expected label.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.config import MODELS_DIR
from componentd.layer2_inference import StressScorer


def expected_label(path: Path) -> str:
    name = path.stem.lower()
    if "calm" in name or "relax" in name:
        return "calm"
    if "stress" in name:
        return "stressed"
    return "?"


def collect_wavs(args: list[str]) -> list[Path]:
    paths = []
    for a in args:
        p = Path(a)
        if p.is_dir():
            paths += sorted(p.glob("*.wav"))
        elif p.suffix == ".wav":
            paths.append(p)
    return paths


def main():
    import argparse
    ap = argparse.ArgumentParser(description="Score real voices with a checkpoint.")
    ap.add_argument("--model", default=str(MODELS_DIR / "fusion_v2.pt"),
                    help="checkpoint to score with (default models/fusion_v2.pt)")
    ap.add_argument("paths", nargs="+", help="wav files or folders to score")
    args = ap.parse_args()

    wavs = collect_wavs(args.paths)
    if not wavs:
        print("no .wav files found in the given path(s)")
        sys.exit(1)

    ckpt = Path(args.model)
    if not ckpt.exists():
        print(f"missing {ckpt} - download it from Drive first")
        sys.exit(1)

    print(f"loading model from {ckpt} (encoder loads on first clip)...")
    scorer = StressScorer(str(ckpt))

    print(f"\n{'file':<35} {'expected':<10} {'stress':>7} {'level':>9} "
          f"{'conf':>6} {'valence':>8} {'arousal':>8}")
    print("-" * 92)
    for wav in wavs:
        report = scorer.score_file(str(wav))
        exp = expected_label(wav)
        # simple correctness marker when we have an expectation to check
        mark = ""
        if exp == "calm":
            mark = "OK" if report["stress_score"] < 5 else "CHECK"
        elif exp == "stressed":
            mark = "OK" if report["stress_score"] >= 5 else "CHECK"
        print(f"{wav.name:<35} {exp:<10} {report['stress_score']:>7.2f} "
              f"{report.get('stress_level', '-'):>9} "
              f"{report['confidence']:>6.2f} {report['valence']:>8.3f} "
              f"{report['arousal']:>8.3f}  {mark}")


if __name__ == "__main__":
    main()
