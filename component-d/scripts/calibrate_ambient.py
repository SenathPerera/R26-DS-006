"""Calibrate the Layer-1 ambient thresholds on REAL device audio.

The AMBIENT dict in config.py ships with uncalibrated starting guesses. The
whole point of PROBLEM 1 is that a phone mic under-gains, so the numbers that
separate a quiet room from a noisy one on THIS device can only be found from
clips recorded on it. This script does not fabricate anything — it reads the
clips you record and reports, per metric, how well it separates the two classes
plus a suggested Youden's-J threshold (the same approach STRESSED_THRESHOLD used).

Usage:
    .venv/bin/python scripts/calibrate_ambient.py <dir>

<dir> must contain two subfolders of clips recorded on the Galaxy A9 with the
same UNPROCESSED capture the app uses (record a room, pull the debug WAVs):

    <dir>/quiet/   at least ~15 clips of rooms you WANT to pass
    <dir>/noisy/   at least ~15 clips you WANT to fail (fan/AC, traffic, chatter),
                   ideally a spread across the noise types you care about

More clips = tighter thresholds. Fewer than ~10 per class and the suggestion is
only directional. Pull debug clips with, e.g.:
    adb exec-out run-as com.mindsyncvr tar c files/voice_debug | tar x
"""

import sys
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from componentd.config import AMBIENT, SAMPLE_RATE
from componentd.layer1_quality import _dbfs, _frame_rms, _spectral_features

# Metrics we can calibrate a threshold for, and the direction that means "worse"
# (a room fails when the metric is ABOVE the threshold, for all of these).
METRICS = ["noise_floor_dbfs", "peak_dbfs", "low_freq_ratio",
           "high_freq_ratio", "dynamic_range_db", "spectral_flatness"]
CONFIG_KEY = {
    "noise_floor_dbfs": "floor_dbfs_max",
    "peak_dbfs": "peak_dbfs_max",
    "low_freq_ratio": "low_freq_ratio_max",
    "high_freq_ratio": "high_freq_ratio_max",
    "dynamic_range_db": "dynamic_range_max_db",
}


def acoustic_metrics(path: Path) -> dict:
    audio, sr = sf.read(str(path), dtype="float32")
    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if sr != SAMPLE_RATE:
        import librosa
        audio = librosa.resample(audio, orig_sr=sr, target_sr=SAMPLE_RATE)
    frames = _frame_rms(audio)
    floor = float(np.percentile(frames, 20))
    peak = float(np.percentile(frames, 95))
    spec = _spectral_features(audio, SAMPLE_RATE)
    return {
        "noise_floor_dbfs": _dbfs(floor),
        "peak_dbfs": _dbfs(peak),
        "dynamic_range_db": _dbfs(peak) - _dbfs(floor),
        "transient_count": int(np.sum(frames > floor * AMBIENT["transient_floor_mult"])),
        **spec,
    }


def load_class(folder: Path) -> list[dict]:
    clips = sorted(p for ext in ("*.wav", "*.WAV", "*.m4a", "*.mp3")
                   for p in folder.glob(ext))
    return [acoustic_metrics(p) for p in clips]


def youden_threshold(quiet_vals, noisy_vals):
    """Best 'fail if value > t' threshold: maximise TPR(noisy) - FPR(quiet)."""
    candidates = sorted(set(quiet_vals + noisy_vals))
    best_t, best_j = None, -1.0
    for t in candidates:
        tpr = np.mean([v > t for v in noisy_vals]) if noisy_vals else 0.0
        fpr = np.mean([v > t for v in quiet_vals]) if quiet_vals else 0.0
        j = tpr - fpr
        if j > best_j:
            best_j, best_t = j, t
    return best_t, best_j


def describe(name, vals):
    a = np.array(vals)
    return (f"    {name:<18} n={len(a):<3} "
            f"min={a.min():7.2f}  median={np.median(a):7.2f}  "
            f"mean={a.mean():7.2f}  max={a.max():7.2f}")


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(1)
    root = Path(sys.argv[1])
    quiet = load_class(root / "quiet")
    noisy = load_class(root / "noisy")
    if not quiet or not noisy:
        print(f"Need clips in {root}/quiet and {root}/noisy "
              f"(found {len(quiet)} quiet, {len(noisy)} noisy).")
        sys.exit(1)

    print(f"\nLoaded {len(quiet)} quiet and {len(noisy)} noisy clips from {root}\n")
    if min(len(quiet), len(noisy)) < 10:
        print("  NOTE: fewer than 10 clips in a class — thresholds are DIRECTIONAL only.\n")

    for m in METRICS:
        q = [c[m] for c in quiet]
        n = [c[m] for c in noisy]
        print(f"[{m}]")
        print(describe("quiet", q))
        print(describe("noisy", n))
        t, j = youden_threshold(q, n)
        cfg = CONFIG_KEY.get(m)
        cur = AMBIENT.get(cfg) if cfg else None
        cur_s = f"  (config {cfg} = {cur})" if cur is not None else ""
        print(f"    -> suggested 'fail if > {t:.2f}'  (Youden's J = {j:.2f}){cur_s}\n")

    print("These are suggestions from YOUR clips, not fabricated defaults. Update the\n"
          "AMBIENT dict in config.py with the values you trust, then re-run the tests.\n")


if __name__ == "__main__":
    main()
