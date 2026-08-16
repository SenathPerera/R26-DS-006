"""Layer 2a: prosodic feature extraction (the trainable branch's input).

Why prosody: pitch rise, jitter/shimmer (voice instability), speech rate
and pausing are PHYSIOLOGICAL stress responses - they appear in any
language and in spontaneous speech. This branch is what lets the model
generalise beyond acted English and, later, transfer to Tamil/Sinhala.
"""

import sys
from pathlib import Path

import librosa
import numpy as np
import parselmouth

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import SAMPLE_RATE

# Fixed feature order. The model reads its input width from this list,
# so adding a feature here automatically resizes the network.
FEATURE_NAMES = [
    # fundamental frequency (pitch) statistics - rises under stress
    "f0_mean", "f0_std", "f0_min", "f0_max", "f0_range", "f0_median",
    # voice quality - cycle-to-cycle instability rises under stress
    "jitter_local", "shimmer_local", "hnr_mean",
    # energy
    "rms_mean", "rms_std", "rms_max",
    # spectral shape
    "zcr_mean", "centroid_mean", "centroid_std", "rolloff_mean", "flatness_mean",
    # timing - stress speeds speech and shortens pauses
    "speech_rate", "pause_ratio", "voiced_fraction",
    "duration_sec",
]
N_FEATURES = len(FEATURE_NAMES)


def _pitch_features(sound: parselmouth.Sound) -> dict:
    """F0 statistics over voiced frames only (unvoiced frames report 0 Hz
    and would corrupt the statistics if included)."""
    pitch = sound.to_pitch(pitch_floor=60.0, pitch_ceiling=500.0)
    f0 = pitch.selected_array["frequency"]
    voiced = f0[f0 > 0]
    if len(voiced) < 2:  # no usable pitch track (e.g. whisper, silence)
        return {k: 0.0 for k in ["f0_mean", "f0_std", "f0_min", "f0_max",
                                 "f0_range", "f0_median", "voiced_fraction"]}
    return {
        "f0_mean": float(np.mean(voiced)),
        "f0_std": float(np.std(voiced)),
        "f0_min": float(np.min(voiced)),
        "f0_max": float(np.max(voiced)),
        "f0_range": float(np.max(voiced) - np.min(voiced)),
        "f0_median": float(np.median(voiced)),
        "voiced_fraction": float(len(voiced) / len(f0)),
    }


def _voice_quality(sound: parselmouth.Sound) -> dict:
    """Jitter (pitch instability), shimmer (amplitude instability) and
    HNR (harmonics-to-noise ratio) via Praat - the same algorithms used
    in clinical voice research."""
    out = {"jitter_local": 0.0, "shimmer_local": 0.0, "hnr_mean": 0.0}
    try:
        pp = parselmouth.praat.call(sound, "To PointProcess (periodic, cc)", 60, 500)
        out["jitter_local"] = float(parselmouth.praat.call(
            pp, "Get jitter (local)", 0, 0, 0.0001, 0.02, 1.3))
        out["shimmer_local"] = float(parselmouth.praat.call(
            [sound, pp], "Get shimmer (local)", 0, 0, 0.0001, 0.02, 1.3, 1.6))
        harmonicity = parselmouth.praat.call(sound, "To Harmonicity (cc)",
                                             0.01, 60, 0.1, 1.0)
        out["hnr_mean"] = float(parselmouth.praat.call(harmonicity, "Get mean", 0, 0))
    except Exception:
        pass  # unvoiced/too-short clip: keep zeros rather than crash
    # Praat returns NaN for undefined measures; the model must never see NaN
    for key, value in out.items():
        if not np.isfinite(value):
            out[key] = 0.0
    return out


def _energy_and_timing(audio: np.ndarray, sr: int) -> dict:
    """Energy, spectral shape, and timing features via librosa."""
    rms = librosa.feature.rms(y=audio)[0]
    zcr = librosa.feature.zero_crossing_rate(y=audio)[0]
    centroid = librosa.feature.spectral_centroid(y=audio, sr=sr)[0]
    rolloff = librosa.feature.spectral_rolloff(y=audio, sr=sr)[0]
    flatness = librosa.feature.spectral_flatness(y=audio)[0]

    # Pause ratio: frames quieter than 10% of the loudest frame.
    pause_ratio = float(np.mean(rms < 0.1 * (np.max(rms) + 1e-10)))

    # Onset rate approximates syllables/second = speech rate.
    onsets = librosa.onset.onset_detect(y=audio, sr=sr)
    speech_rate = float(len(onsets) / (len(audio) / sr))

    return {
        "rms_mean": float(np.mean(rms)),
        "rms_std": float(np.std(rms)),
        "rms_max": float(np.max(rms)),
        "zcr_mean": float(np.mean(zcr)),
        "centroid_mean": float(np.mean(centroid)),
        "centroid_std": float(np.std(centroid)),
        "rolloff_mean": float(np.mean(rolloff)),
        "flatness_mean": float(np.mean(flatness)),
        "speech_rate": speech_rate,
        "pause_ratio": pause_ratio,
    }


def extract_prosody(audio: np.ndarray, sr: int = SAMPLE_RATE) -> np.ndarray:
    """One audio clip -> feature vector in FEATURE_NAMES order.
    Guaranteed finite (NaN/inf replaced by 0) so it is always model-safe."""
    audio = np.asarray(audio, dtype=np.float64).flatten()
    sound = parselmouth.Sound(audio, sampling_frequency=sr)

    features = {}
    features.update(_pitch_features(sound))
    features.update(_voice_quality(sound))
    features.update(_energy_and_timing(audio.astype(np.float32), sr))
    features["duration_sec"] = float(len(audio) / sr)

    vector = np.array([features[name] for name in FEATURE_NAMES], dtype=np.float32)
    return np.nan_to_num(vector, nan=0.0, posinf=0.0, neginf=0.0)


def extract_prosody_file(path: str) -> np.ndarray:
    """Convenience: load any audio file (any rate/format) and extract."""
    audio, _ = librosa.load(path, sr=SAMPLE_RATE, mono=True)
    return extract_prosody(audio, SAMPLE_RATE)
