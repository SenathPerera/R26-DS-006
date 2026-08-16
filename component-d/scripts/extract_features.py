"""Training stage 1: extract and cache features for every clip.

Runs the FROZEN encoder + prosody extractor once per clip and saves
everything to one compressed .npz file. Training (stage 2) then never
touches raw audio again - this is why every later experiment is fast.

This is the slow step: run it on Colab GPU (or overnight on CPU).

Usage:
  python scripts/extract_features.py \
      --metadata data/metadata_meld.csv \
      --out data/features_meld.npz --encoder plus_large
"""

import argparse
import sys
from pathlib import Path

import librosa
import numpy as np
import pandas as pd
from tqdm import tqdm

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.config import ENCODER_ABLATIONS, SAMPLE_RATE
from componentd.layer2_prosody import N_FEATURES, extract_prosody
from componentd.preprocessing import prepare


def load_encoder(encoder_key: str):
    # Imported here (not top of file) so every other part of this script
    # works on machines without funasr installed.
    from funasr import AutoModel
    model_id = ENCODER_ABLATIONS[encoder_key]
    print(f"loading frozen encoder: {model_id}")
    return AutoModel(model=model_id, hub="ms", disable_update=True)


def embed_clip(encoder, audio: np.ndarray) -> np.ndarray:
    """One clip -> one utterance-level embedding from the frozen encoder."""
    result = encoder.generate(audio, granularity="utterance",
                              extract_embedding=True, disable_pbar=True)
    return np.asarray(result[0]["feats"], dtype=np.float32)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--metadata", nargs="+", required=True,
                    help="one or more metadata csvs to process together")
    ap.add_argument("--out", required=True, help="output .npz path")
    ap.add_argument("--encoder", default="plus_large",
                    choices=ENCODER_ABLATIONS)
    ap.add_argument("--augment", type=int, default=0,
                    help="domain-augmented copies per TRAIN clip (0 = off); "
                         "makes studio audio resemble phone recordings")
    args = ap.parse_args()

    meta = pd.concat([pd.read_csv(m) for m in args.metadata],
                     ignore_index=True)
    print(f"{len(meta)} clips from {len(args.metadata)} metadata file(s)")

    encoder = load_encoder(args.encoder)

    augment_fn = None
    if args.augment > 0:
        from componentd.augmentation import augment as augment_fn
        print(f"domain augmentation ON: +{args.augment} phone-like copies per "
              f"TRAIN clip (val/test stay clean)")

    rng = np.random.RandomState(0)
    embeddings, prosody, rows = [], [], []
    for _, row in tqdm(meta.iterrows(), total=len(meta), desc="extracting"):
        try:
            raw, _ = librosa.load(row["path"], sr=SAMPLE_RATE, mono=True)
        except Exception as e:
            print(f"skip {row['path']}: {e}")
            continue
        # Clean version always; augmented copies for TRAIN clips only.
        versions = [raw]
        if augment_fn is not None and str(row.get("split", "train")) == "train":
            versions += [augment_fn(raw, SAMPLE_RATE, rng)
                         for _ in range(args.augment)]
        for v in versions:
            try:
                # Same conditioning as inference; augmentation ran BEFORE it,
                # so the degraded (phone-like) audio is conditioned identically.
                audio, ok, _ = prepare(v, SAMPLE_RATE)
                if not ok:                 # too short after trimming
                    continue
                embeddings.append(embed_clip(encoder, audio))
                prosody.append(extract_prosody(audio))
                rows.append(row)
            except Exception as e:
                print(f"skip {row['path']}: {e}")

    emb = np.stack(embeddings)
    pros = np.stack(prosody)
    assert pros.shape[1] == N_FEATURES
    out_meta = pd.DataFrame(rows).reset_index(drop=True)

    # One file carries features + every label/grouping column training needs.
    np.savez_compressed(
        args.out,
        emb=emb, pros=pros,
        valence=out_meta["valence"].to_numpy(np.float32),
        arousal=out_meta["arousal"].to_numpy(np.float32),
        stress01=out_meta["stress01"].to_numpy(np.float32),
        stress_label=out_meta["stress_label"].to_numpy(np.int64),
        split=out_meta["split"].to_numpy(str),
        language=out_meta["language"].to_numpy(str),
        dataset=out_meta["dataset"].to_numpy(str),
        speaker=out_meta["speaker"].to_numpy(str),
        encoder=args.encoder,
    )
    print(f"saved {len(out_meta)} feature rows -> {args.out}")
    print(f"embedding dim {emb.shape[1]}, prosody dim {pros.shape[1]}")


if __name__ == "__main__":
    main()
