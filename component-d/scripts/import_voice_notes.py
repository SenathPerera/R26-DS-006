"""Import voice notes (WhatsApp / any format): play each one, you label
it calm or stressed, and it is converted to 16 kHz mono wav and saved
with the naming convention the training/eval pipeline expects:

    <person>_<condition>_<NN>.wav      e.g. p1_stressed_01.wav

WhatsApp notes are usually .opus/.ogg/.m4a - ffmpeg handles all of them.

Usage:
  # training data (default): goes to data/raw/real_collected/
  python scripts/import_voice_notes.py --input ~/Downloads/notes --person p1

  # held-out test data: goes to data/real_voice_eval/
  python scripts/import_voice_notes.py --input ~/Downloads/notes --person p9 --purpose test

Per file you press:  c = calm,  s = stressed,  r = replay,  k = skip,  q = quit
"""

import argparse
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.config import ROOT, SAMPLE_RATE

# formats WhatsApp / phones commonly produce
AUDIO_EXTS = {".opus", ".ogg", ".m4a", ".mp3", ".wav", ".aac", ".mp4", ".amr"}

DEST = {
    "train": ROOT / "data" / "raw" / "real_collected",
    "test": ROOT / "data" / "real_voice_eval",
}


def play(path: Path):
    """Play the note so you can hear it before labelling (macOS afplay).
    Silently does nothing if afplay is unavailable."""
    try:
        subprocess.run(["afplay", str(path)], check=False)
    except FileNotFoundError:
        print("  (afplay not found - open the file manually to listen)")


def convert(src: Path, dst: Path) -> bool:
    """Any audio format -> 16 kHz mono wav via ffmpeg."""
    dst.parent.mkdir(parents=True, exist_ok=True)
    cmd = ["ffmpeg", "-y", "-i", str(src), "-ar", str(SAMPLE_RATE),
           "-ac", "1", "-loglevel", "error", str(dst)]
    return subprocess.run(cmd).returncode == 0


def next_number(dest: Path, person: str, condition: str) -> int:
    """Next free NN for this person+condition, so files never overwrite."""
    existing = list(dest.glob(f"{person}_{condition}_*.wav"))
    return len(existing) + 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True, help="folder of voice notes")
    ap.add_argument("--person", required=True,
                    help="speaker id for this batch, e.g. p1 (keep one id "
                         "per real person - the model uses it for "
                         "speaker-independent splitting)")
    ap.add_argument("--purpose", choices=["train", "test"], default="train")
    ap.add_argument("--no-play", action="store_true",
                    help="do not auto-play each note")
    args = ap.parse_args()

    dest = DEST[args.purpose]
    dest.mkdir(parents=True, exist_ok=True)
    src_files = sorted(p for p in Path(args.input).iterdir()
                       if p.suffix.lower() in AUDIO_EXTS)
    if not src_files:
        print(f"no audio files found in {args.input}")
        return

    print(f"{len(src_files)} notes to label -> saving to {dest}")
    print(f"speaker: {args.person}  |  purpose: {args.purpose}\n")

    saved = 0
    for i, src in enumerate(src_files, 1):
        print(f"[{i}/{len(src_files)}] {src.name}")
        if not args.no_play:
            play(src)

        while True:
            choice = input("  label  c=calm  s=stressed  r=replay  "
                           "k=skip  q=quit : ").strip().lower()
            if choice == "r":
                play(src)
                continue
            if choice == "q":
                print(f"\nstopped. saved {saved} clips to {dest}")
                return
            if choice == "k":
                print("  skipped\n")
                break
            if choice in ("c", "s"):
                condition = "calm" if choice == "c" else "stressed"
                n = next_number(dest, args.person, condition)
                out = dest / f"{args.person}_{condition}_{n:02d}.wav"
                if convert(src, out):
                    saved += 1
                    print(f"  saved -> {out.name}\n")
                else:
                    print("  ffmpeg failed - skipped\n")
                break
            print("  (please press c, s, r, k, or q)")

    print(f"done. saved {saved} clips to {dest}")


if __name__ == "__main__":
    main()
