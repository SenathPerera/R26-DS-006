"""Layer 2c: inference wrapper - what the API server actually calls.

Loads a trained checkpoint once, keeps the frozen encoder in memory,
and turns raw audio into the final stress report:
    {stress_score 0-10, confidence, valence, arousal, gate_mean}
"""

import sys
from pathlib import Path

import librosa
import numpy as np
import torch

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import (ENCODER_ABLATIONS, SAMPLE_RATE, confidence_from_va,
                    stress_from_va, stress_level_from_score, stress_type_from_va)
from componentd.layer2_fusion import GatedFusionModel
from componentd.layer2_prosody import extract_prosody
from componentd.preprocessing import prepare


class StressScorer:
    """audio -> stress report. One instance per server process."""

    def __init__(self, checkpoint_path: str, device: str | None = None):
        self.device = device or ("cuda" if torch.cuda.is_available() else "cpu")

        # The checkpoint carries everything needed to rebuild the model:
        # architecture sizes, weights, and the prosody normalisation stats
        # computed at training time (must be identical at inference).
        ckpt = torch.load(checkpoint_path, map_location=self.device,
                          weights_only=False)
        cfg = ckpt["fusion_config"]
        self.model = GatedFusionModel(
            emb_dim=ckpt["emb_dim"], pros_dim=ckpt["pros_dim"],
            proj_dim=cfg["proj_dim"], hidden_dim=cfg["hidden_dim"],
            dropout=cfg["dropout"],
        ).to(self.device)
        self.model.load_state_dict(ckpt["state_dict"])
        self.model.eval()

        self.pros_mean = np.asarray(ckpt["pros_mean"], dtype=np.float32)
        self.pros_std = np.asarray(ckpt["pros_std"], dtype=np.float32)

        # Encoder loads lazily on first request (heavy import + weights),
        # so server startup stays fast.
        self._encoder = None
        self._encoder_id = ENCODER_ABLATIONS[ckpt.get("encoder", "plus_large")]

    def _get_encoder(self):
        if self._encoder is None:
            from funasr import AutoModel
            self._encoder = AutoModel(model=self._encoder_id, hub="ms",
                                      disable_update=True)
        return self._encoder

    def score_array(self, audio: np.ndarray, sr: int = SAMPLE_RATE) -> dict:
        """Score one clip already loaded as a numpy array. Runs the SAME
        preprocessing conditioning used at training time (resample, DC
        removal, high-pass, silence trim, loudness norm, duration cap) so
        the model sees exactly what it was trained on - no train/serve skew."""
        audio, _ok, _reason = prepare(audio, sr)

        # Branch 1: frozen encoder -> utterance-level emotion embedding
        result = self._get_encoder().generate(
            audio, granularity="utterance", extract_embedding=True,
            disable_pbar=True)
        emb = torch.from_numpy(
            np.asarray(result[0]["feats"], dtype=np.float32)).unsqueeze(0)

        # Branch 2: prosody, standardised with the TRAINING-time stats
        pros_raw = extract_prosody(audio)
        pros = (pros_raw - self.pros_mean) / (self.pros_std + 1e-8)
        pros = torch.from_numpy(pros.astype(np.float32)).unsqueeze(0)

        with torch.no_grad():
            v, a, gate = self.model(emb.to(self.device), pros.to(self.device))
        valence, arousal = float(v[0]), float(a[0])

        stress01 = stress_from_va(valence, arousal)
        score = round(stress01 * 10, 2)
        # Confidence tracks the RELIABLE axis: |valence|. A prediction whose
        # valence sits near neutral is genuinely ambiguous -> low confidence, and
        # Layer 4 should defer to Component B there. (The old sqrt(v^2+a^2) was
        # inflated by confidently-wrong arousal on exactly the collapse cases.)
        confidence = float(confidence_from_va(valence, arousal))

        return {
            "stress_score": score,                                  # 0-10 continuous
            "stress_level": stress_level_from_score(score),         # no/mild/moderate/high
            "stress_type": stress_type_from_va(valence, arousal),   # activated/shutdown/None
            "confidence": round(confidence, 3),
            "valence": round(valence, 3),
            "arousal": round(arousal, 3),
            "gate_mean": round(float(gate.mean()), 3),
        }

    def score_file(self, path: str) -> dict:
        """Score one audio file of any format/sample rate."""
        audio, sr = librosa.load(path, sr=SAMPLE_RATE, mono=True)
        return self.score_array(audio, sr)
