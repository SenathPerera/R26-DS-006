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
from componentd.config import (AMBIENT, MIN_DURATION_SEC, QUALITY, SAMPLE_RATE,
                                VAD_THRESHOLD)

# Framing for the ambient acoustic analysis. 512 samples = 32 ms at 16 kHz, a
# 256-sample hop = 50% overlap — fine enough to resolve a steady noise floor and
# short transients (doors, clicks) without being dominated by any single frame.
_FRAME = 512
_HOP = 256

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


def _dbfs(x: float) -> float:
    """Linear amplitude → dBFS, floored so log(0) can't blow up."""
    return float(20.0 * np.log10(max(float(x), 1e-7)))


def _frame_rms(audio: np.ndarray) -> np.ndarray:
    """Per-frame RMS over 32 ms windows. The distribution of these (its low
    percentile = steady floor, high percentile = peaks) is what a single
    whole-clip mean throws away — and that mean is exactly what let noisy
    rooms through before."""
    n = len(audio)
    if n < _FRAME:
        return np.array([_rms(audio)], dtype=np.float64)
    idx = range(0, n - _FRAME + 1, _HOP)
    return np.array([_rms(audio[i:i + _FRAME]) for i in idx], dtype=np.float64)


def _spectral_features(audio: np.ndarray, sr: int) -> dict:
    """Spectral shape of the room: flatness (broadband hiss ≈1 vs tonal hum ≈0)
    and the fraction of energy in the low (<300 Hz: fans, AC, rumble) and high
    (>4 kHz: electrical hiss, buzz) bands. One scalar RMS cannot tell any of
    these apart — this is the analysis PROBLEM 1 was missing entirely."""
    import librosa
    y = np.ascontiguousarray(audio, dtype=np.float32)
    flatness = float(np.mean(librosa.feature.spectral_flatness(
        y=y, n_fft=_FRAME, hop_length=_HOP)))
    power = np.abs(librosa.stft(y, n_fft=_FRAME, hop_length=_HOP)) ** 2
    freqs = librosa.fft_frequencies(sr=sr, n_fft=_FRAME)
    total = float(power.sum()) + 1e-12
    low = float(power[freqs < 300.0].sum()) / total
    high = float(power[freqs > 4000.0].sum()) / total
    return {"spectral_flatness": round(flatness, 4),
            "low_freq_ratio": round(low, 4),
            "high_freq_ratio": round(high, 4)}


def _classify_noise(m: dict) -> str:
    """Name the dominant noise so the companion can give a concrete suggestion
    instead of a vague 'it's noisy'. Voices win over everything (privacy)."""
    if m["speech_seconds"] > AMBIENT["max_speech_sec"]:
        return "voices"
    if m["low_freq_ratio"] > 0.65 and m["spectral_flatness"] < 0.35:
        return "hum"            # fan, AC, motor, fridge (tonal, low-frequency)
    floor_bad = m["noise_floor_dbfs"] > AMBIENT["floor_dbfs_max"]
    # A FLAT spectrum spanning every band is broadband (traffic, rain, crowd);
    # test this before hiss, since flat noise also has substantial high-band
    # energy and would otherwise be misread as electrical hiss.
    if m["spectral_flatness"] > 0.45 and floor_bad:
        return "broadband"
    if m["high_freq_ratio"] > 0.40:
        return "hiss"           # electronics, fluorescent light (high-skewed)
    if m["transient_count"] >= 3:
        return "intermittent"   # doors, footsteps, clatter
    return "quiet"


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
    """The 'please stay silent' step, rewritten to do REAL acoustic analysis
    (PROBLEM 1). Instead of one whole-clip mean RMS — which never fired on an
    UNPROCESSED phone mic — it frames the clip and judges the *steady* noise
    floor (a low percentile), transient peaks, spectral shape (hum vs hiss vs
    broadband), and nearby speech, then names the noise type so the companion
    can suggest something specific.

    Backward-compatible: ``ok``, ``reasons`` and the original ``metrics`` keys
    are preserved exactly (the web client + existing tests depend on them).
    ``score``, ``noise_type`` and ``checks`` are purely additive.
    vad_fn defaults to real Silero VAD; tests inject a fake."""
    vad_fn = vad_fn or speech_segments
    audio = np.asarray(audio, dtype=np.float32).flatten()

    duration = len(audio) / sr
    segments = vad_fn(audio, sr)
    metrics = _compute_metrics(audio, sr, segments)

    # Framewise level statistics — the steady floor is a low percentile, not a
    # mean, so a few transients can't drag it up and hide a bad room.
    frames = _frame_rms(audio)
    floor_rms = float(np.percentile(frames, 20))
    peak_rms = float(np.percentile(frames, 95))
    median_rms = float(np.percentile(frames, 50))
    floor_dbfs = _dbfs(floor_rms)
    peak_dbfs = _dbfs(peak_rms)
    transient_count = int(np.sum(frames > floor_rms * AMBIENT["transient_floor_mult"]))
    spec = _spectral_features(audio, sr)
    clip = metrics["clip_ratio"]

    metrics.update({
        "noise_floor_rms": round(floor_rms, 6),
        "noise_floor_dbfs": round(floor_dbfs, 2),
        "peak_dbfs": round(peak_dbfs, 2),
        "median_dbfs": round(_dbfs(median_rms), 2),
        "dynamic_range_db": round(peak_dbfs - floor_dbfs, 2),
        "transient_count": transient_count,
        **spec,
    })

    noise_type = _classify_noise(metrics)

    # Per-check results. severity "fail" gates the room; "warn" is advisory and
    # never blocks on its own. reasons[] keeps the original substrings so callers
    # and tests that match on them keep working.
    checks: list[dict] = []
    reasons: list[str] = []

    def add(cid, label, value, unit, ok, severity, message, reason=None):
        checks.append({"id": cid, "label": label, "value": round(float(value), 3),
                       "unit": unit, "pass": bool(ok), "severity": severity,
                       "message": message})
        if not ok and reason:
            reasons.append(reason)

    # Steady background noise only BLOCKS when it's genuinely loud (above
    # floor_too_noisy); a fan/AC in the "usable" band passes and is compensated
    # for at Layer 2. Human speech + clipping below remain hard fails.
    floor_ok = floor_dbfs <= AMBIENT["floor_too_noisy"]
    add("noise_floor", "Background noise", floor_dbfs, "dBFS",
        floor_ok, "fail",
        _floor_message(noise_type, floor_ok),
        f"too_noisy: noise floor {floor_dbfs:.1f} dBFS > {AMBIENT['floor_too_noisy']:.0f} dBFS")
    # A single door/clatter shouldn't ruin 30s of speech: transients are advisory.
    add("peaks", "Sudden sounds", peak_dbfs, "dBFS",
        peak_dbfs <= AMBIENT["peak_dbfs_max"], "warn",
        ("A few sudden sounds came through — let's wait for things to settle."
         if peak_dbfs > AMBIENT["peak_dbfs_max"] else "No sudden sounds — good."))
    add("voices", "Nearby speech", metrics["speech_seconds"], "s",
        metrics["speech_seconds"] <= AMBIENT["max_speech_sec"], "fail",
        ("I can hear someone talking nearby — somewhere more private would help."
         if metrics["speech_seconds"] > AMBIENT["max_speech_sec"] else "No nearby voices — good."),
        f"voice_detected: {metrics['speech_seconds']:.2f}s of speech found "
        f"- please ensure nobody is talking nearby")
    add("tonal_noise", "Hum", metrics["low_freq_ratio"], "ratio",
        metrics["low_freq_ratio"] <= AMBIENT["low_freq_ratio_max"], "warn",
        ("There's a steady low hum — it may be a fan or air conditioning."
         if metrics["low_freq_ratio"] > AMBIENT["low_freq_ratio_max"] else "No tonal hum — good."))
    add("clipping", "Distortion", clip, "ratio",
        clip <= AMBIENT["max_clip_ratio"], "fail",
        ("The mic is overloading — try moving it slightly further away."
         if clip > AMBIENT["max_clip_ratio"] else "No distortion — good."),
        f"clipping: {clip:.3f} of samples at ceiling")
    add("duration", "Sample length", duration, "s",
        duration >= AMBIENT["min_duration_sec"], "fail",
        ("That sample was too short — let's listen a little longer."
         if duration < AMBIENT["min_duration_sec"] else "Enough audio to judge — good."),
        f"too_short: {duration:.2f}s < {AMBIENT['min_duration_sec']}s")

    # Coherent three-state verdict, so the number and the outcome always agree
    # (BUG-C: no more "90/100" shown next to a FAIL). Order: contamination we
    # cannot compensate for (voices, clipping) first, then steady-noise banding.
    speech_ok = metrics["speech_seconds"] <= AMBIENT["max_speech_sec"]
    clip_ok = clip <= AMBIENT["max_clip_ratio"]
    if not speech_ok:
        verdict = "voices"
    elif not clip_ok:
        verdict = "clipping"
    elif floor_dbfs > AMBIENT["floor_too_noisy"]:
        verdict = "too_noisy"
    elif floor_dbfs > AMBIENT["floor_good_max"]:
        verdict = "usable"
    else:
        verdict = "good"

    ok = all(c["pass"] for c in checks if c["severity"] == "fail")
    score = _ambient_score(metrics)
    if not ok:
        score = min(score, 40)   # a blocked room must never display a passing score

    return {"ok": ok, "score": score, "noise_type": noise_type, "verdict": verdict,
            "reasons": reasons, "checks": checks, "metrics": metrics}


def _floor_message(noise_type: str, ok: bool) -> str:
    if ok:
        return "The room is quiet enough for a clean recording."
    return {
        "hum": "There's a steady background sound — it may be a fan or air conditioning.",
        "broadband": "There's steady background noise — traffic or a window onto the street, perhaps.",
        "hiss": "There's a faint electrical hiss in the background.",
        "intermittent": "There's some on-and-off background sound around you.",
        "voices": "It's not quite quiet enough — and I can hear voices nearby.",
    }.get(noise_type, "There's more background sound than is ideal for a clean reading.")


def _ambient_score(metrics: dict) -> int:
    """0–100 display roll-up (never overrides a failed check). Weighted:
    noise floor 40%, peaks 20%, nearby voices 20%, tonal character 10%,
    clipping + duration 10%. Each component is 1.0 at its target, decaying to 0
    as it worsens past a soft ceiling."""
    def band(value, good, bad):  # 1.0 at/below good, 0.0 at/above bad
        if value <= good:
            return 1.0
        if value >= bad:
            return 0.0
        return 1.0 - (value - good) / (bad - good)

    floor = band(metrics["noise_floor_dbfs"], AMBIENT["floor_dbfs_max"], AMBIENT["floor_dbfs_max"] + 20)
    peaks = band(metrics["peak_dbfs"], AMBIENT["peak_dbfs_max"], AMBIENT["peak_dbfs_max"] + 20)
    voices = band(metrics["speech_seconds"], AMBIENT["max_speech_sec"], AMBIENT["max_speech_sec"] + 3.0)
    tonal = band(metrics["low_freq_ratio"], AMBIENT["low_freq_ratio_max"], 1.0)
    clip_ok = 1.0 if metrics["clip_ratio"] <= AMBIENT["max_clip_ratio"] else 0.0
    dur_ok = 1.0 if metrics["duration_sec"] >= AMBIENT["min_duration_sec"] else 0.0
    score = 100 * (0.40 * floor + 0.20 * peaks + 0.20 * voices
                   + 0.10 * tonal + 0.05 * clip_ok + 0.05 * dur_ok)
    return int(round(max(0.0, min(100.0, score))))


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
