"""Layer 1: audio quality gate.

Two DIFFERENT checks, not one reused function - see the QUALITY comment
in config.py for why. Voice-activity detection (Silero VAD, a small
frozen pretrained model) is the core signal for both; it answers "is
there a human voice here" robustly across arbitrary real-world noise
(fans, traffic, chatter, hums) in a way hand-tuned spectral thresholds
cannot generalise to. Simple DSP checks (RMS floor, clipping) catch the
cases VAD does not: silence, a muted mic, or mic overload.
"""

import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import MIN_DURATION_SEC, QUALITY, SAMPLE_RATE, VAD_THRESHOLD

_vad_model = None
_vad_get_timestamps = None


def _load_vad():
    """Lazy-load Silero VAD once per process. The silero-vad pip package
    BUNDLES the model weights (~1.8MB, MIT licensed) - no network, no
    torch.hub / GitHub download at runtime, so the component runs
    reliably offline (a torch.hub approach failed here on an SSL cert)."""
    global _vad_model, _vad_get_timestamps
    if _vad_model is None:
        from silero_vad import get_speech_timestamps, load_silero_vad
        _vad_model = load_silero_vad()
        _vad_get_timestamps = get_speech_timestamps
    return _vad_model, _vad_get_timestamps


def speech_segments(audio: np.ndarray, sr: int = SAMPLE_RATE) -> list[dict]:
    """Real VAD: [{'start': sample, 'end': sample}, ...] for detected
    speech. Empty list means no speech was found anywhere in the clip.
    This is the default `vad_fn` for both checks below; tests inject a
    fast fake instead so the suite stays offline and deterministic."""
    import torch
    model, get_timestamps = _load_vad()
    audio_t = torch.from_numpy(np.asarray(audio, dtype=np.float32))
    return get_timestamps(audio_t, model, sampling_rate=sr,
                          threshold=VAD_THRESHOLD)


def _rms(audio: np.ndarray) -> float:
    return float(np.sqrt(np.mean(audio ** 2)))


def _clip_ratio(audio: np.ndarray) -> float:
    # fraction of samples sitting at or near the digital ceiling
    return float(np.mean(np.abs(audio) > 0.99))


def _compute_metrics(audio: np.ndarray, sr: int, segments: list[dict]) -> dict:
    duration = len(audio) / sr
    speech_sec = sum(seg["end"] - seg["start"] for seg in segments) / sr
    return {
        "duration_sec": round(duration, 2),
        "rms": round(_rms(audio), 5),
        "clip_ratio": round(_clip_ratio(audio), 4),
        "speech_seconds": round(speech_sec, 2),
        "speech_fraction": round(speech_sec / duration, 3) if duration > 0 else 0.0,
        "speech_segments": len(segments),
    }


def check_ambient(audio: np.ndarray, sr: int = SAMPLE_RATE,
                  vad_fn=None) -> dict:
    """The 'please stay silent' step. Fails if the room is loud OR if
    ANY voice is detected, regardless of what kind of noise it is.
    vad_fn defaults to real Silero VAD; tests inject a fake."""
    vad_fn = vad_fn or speech_segments
    audio = np.asarray(audio, dtype=np.float32).flatten()
    reasons = []

    duration = len(audio) / sr
    if duration < MIN_DURATION_SEC:
        reasons.append(f"too_short: {duration:.2f}s < {MIN_DURATION_SEC}s")

    rms = _rms(audio)
    if rms > QUALITY["ambient_max_rms"]:
        reasons.append(f"too_noisy: rms {rms:.4f} > {QUALITY['ambient_max_rms']}")

    clip = _clip_ratio(audio)
    if clip > QUALITY["max_clip_ratio"]:
        reasons.append(f"clipping: {clip:.3f} of samples at ceiling")

    segments = vad_fn(audio, sr)
    metrics = _compute_metrics(audio, sr, segments)
    if metrics["speech_seconds"] > QUALITY["ambient_max_speech_sec"]:
        reasons.append(
            f"voice_detected: {metrics['speech_seconds']:.2f}s of speech "
            f"found - please ensure nobody is talking nearby")

    return {"ok": len(reasons) == 0, "reasons": reasons, "metrics": metrics}


def check_speech(audio: np.ndarray, sr: int = SAMPLE_RATE,
                 vad_fn=None) -> dict:
    """The pre/post voice recording step. Fails if the clip is too quiet,
    too loud, clipped, or does not contain enough detected speech.
    vad_fn defaults to real Silero VAD; tests inject a fake."""
    vad_fn = vad_fn or speech_segments
    audio = np.asarray(audio, dtype=np.float32).flatten()
    reasons = []

    duration = len(audio) / sr
    if duration < MIN_DURATION_SEC:
        reasons.append(f"too_short: {duration:.2f}s < {MIN_DURATION_SEC}s")

    rms = _rms(audio)
    if rms < QUALITY["speech_min_rms"]:
        reasons.append(f"too_quiet: rms {rms:.4f}")
    if rms > QUALITY["speech_max_rms"]:
        reasons.append(f"too_loud: rms {rms:.4f}")

    clip = _clip_ratio(audio)
    if clip > QUALITY["max_clip_ratio"]:
        reasons.append(f"clipping: {clip:.3f} of samples at ceiling")

    segments = vad_fn(audio, sr)
    metrics = _compute_metrics(audio, sr, segments)
    if metrics["speech_fraction"] < QUALITY["speech_min_fraction"]:
        pct = metrics["speech_fraction"] * 100
        reasons.append(
            f"insufficient_speech: only {pct:.0f}% of the clip contains "
            f"detected speech (need {QUALITY['speech_min_fraction']*100:.0f}%)")

    return {"ok": len(reasons) == 0, "reasons": reasons, "metrics": metrics}
