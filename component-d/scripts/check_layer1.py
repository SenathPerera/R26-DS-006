"""Manual verification of Layer 1 with the REAL Silero VAD.

The unit tests use fake VADs (offline, deterministic). This script
exercises the actual pretrained model on real audio, which is the only
way to confirm VAD genuinely separates speech from noise.

Usage:
  .venv/bin/python scripts/check_layer1.py <wav_or_folder...>
"""

import sys
from pathlib import Path

import librosa
import numpy as np

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.config import SAMPLE_RATE
from componentd.layer1_quality import check_ambient, check_speech


def load(path):
    audio, _ = librosa.load(path, sr=SAMPLE_RATE, mono=True)
    return audio.astype(np.float32)


def synthetic_silence(seconds=3.0):
    rng = np.random.RandomState(0)
    return (0.006 * rng.randn(int(SAMPLE_RATE * seconds))).astype(np.float32)


def report(name, audio):
    amb = check_ambient(audio)
    sp = check_speech(audio)
    print(f"\n{name}")
    print(f"  ambient-check: {'PASS (quiet)' if amb['ok'] else 'FAIL'}  "
          f"{amb['reasons']}")
    print(f"  speech-check : {'PASS (has voice)' if sp['ok'] else 'FAIL'}  "
          f"{sp['reasons']}")
    print(f"  metrics: {sp['metrics']}")


def main():
    print("loading Silero VAD (first run downloads ~1.8MB)...")
    # synthetic near-silence: should PASS ambient, FAIL speech
    report("[synthetic silence]", synthetic_silence())

    for arg in sys.argv[1:]:
        p = Path(arg)
        wavs = sorted(p.glob("*.wav")) if p.is_dir() else [p]
        for wav in wavs:
            report(wav.name, load(wav))


if __name__ == "__main__":
    main()
