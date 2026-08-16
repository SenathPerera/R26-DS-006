"""Standardise the collected real voice notes into the project layout.

The clips arrived from friends as WhatsApp .ogg with inconsistent names
(calm-english-007, calm_sinhala_1, sinhala-stress-01, ...). This converts
them to 16 kHz mono wav with one naming convention and routes them by our
data plan:

  English  -> data/raw/real_collected/   (TRAINING augmentation)
  Sinhala  -> data/real_voice_eval/      (HELD-OUT zero-shot evaluation)

Speaker is inferred from each clip's number pattern (3-digit = p1 and
bilingual, single-digit = p2, 2-digit = p3). Only FORMAT is changed here;
loudness/high-pass conditioning is applied later by src.preprocessing, so
every dataset (studio + phone) is conditioned identically downstream.

Sinhala is deliberately kept out of training so it stays a true zero-shot
test of the language-independent prosody branch. Note p1 is bilingual, so
p1's voice appears in English training AND Sinhala eval - the LANGUAGE is
still unseen (zero-shot), which is the claim we evaluate.

Usage:
  python scripts/prepare_real_voice_data.py --src ~/Desktop/real_voice_data
"""

import argparse
from pathlib import Path

import librosa
import soundfile as sf

# original filename -> (destination subdir relative to data/, new name)
MAPPING = {
    # English -> training (speaker p1)
    "stress-english-001.ogg": ("raw/real_collected", "p1_stressed_01.wav"),
    "stress-english-002.ogg": ("raw/real_collected", "p1_stressed_02.wav"),
    "stress-english-003.ogg": ("raw/real_collected", "p1_stressed_03.wav"),
    "calm-english-007.ogg":   ("raw/real_collected", "p1_calm_01.wav"),
    "calm-english-008.ogg":   ("raw/real_collected", "p1_calm_02.wav"),
    "calm-english-009.ogg":   ("raw/real_collected", "p1_calm_03.wav"),
    # Sinhala stress -> held-out zero-shot eval
    "stress-sinhala-004.ogg": ("real_voice_eval/stress_voice", "sinhala_p1_stressed_01.wav"),
    "strees-sinhala-005.ogg": ("real_voice_eval/stress_voice", "sinhala_p1_stressed_02.wav"),
    "stress-sinhala-006.ogg": ("real_voice_eval/stress_voice", "sinhala_p1_stressed_03.wav"),
    "stress_sinhala_1.ogg":   ("real_voice_eval/stress_voice", "sinhala_p2_stressed_01.wav"),
    "stress_sinhala_2.ogg":   ("real_voice_eval/stress_voice", "sinhala_p2_stressed_02.wav"),
    "stress_sinhala_3.ogg":   ("real_voice_eval/stress_voice", "sinhala_p2_stressed_03.wav"),
    "sinhala-stress-01.ogg":  ("real_voice_eval/stress_voice", "sinhala_p3_stressed_01.wav"),
    "sinhala-stress-02.ogg":  ("real_voice_eval/stress_voice", "sinhala_p3_stressed_02.wav"),
    # Sinhala calm -> held-out zero-shot eval (speaker p2)
    "calm_sinhala_1.ogg":     ("real_voice_eval/calm_voice", "sinhala_p2_calm_01.wav"),
    "calm_sinhala_2.ogg":     ("real_voice_eval/calm_voice", "sinhala_p2_calm_02.wav"),
    "calm_sinhala_3.ogg":     ("real_voice_eval/calm_voice", "sinhala_p2_calm_03.wav"),
}

ROOT = Path(__file__).parent.parent
DATA = ROOT / "data"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True, help="folder of raw .ogg voice notes")
    args = ap.parse_args()
    src_dir = Path(args.src).expanduser()

    done = 0
    for src_name, (subdir, dst_name) in MAPPING.items():
        src = src_dir / src_name
        if not src.exists():
            print(f"missing (skipped): {src_name}")
            continue
        dst_dir = DATA / subdir
        dst_dir.mkdir(parents=True, exist_ok=True)
        audio, _ = librosa.load(str(src), sr=16000, mono=True)
        sf.write(str(dst_dir / dst_name), audio, 16000, subtype="PCM_16")
        done += 1

    print(f"standardised {done}/{len(MAPPING)} clips")
    print("  English -> data/raw/real_collected (training)")
    print("  Sinhala -> data/real_voice_eval (held-out zero-shot eval)")


if __name__ == "__main__":
    main()
