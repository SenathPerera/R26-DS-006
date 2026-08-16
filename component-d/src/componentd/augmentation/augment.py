"""Domain augmentation - make clean STUDIO training audio look like real
PHONE / spontaneous recordings.

Why this exists: our 4-way ablation (MELD, acted, IEMOCAP) showed that every
model hedges toward neutral on real voices no matter how good the labels are -
because all the training audio is clean studio audio, while real users record
on phones (WhatsApp/Opus, room noise, phone bandwidth). That acoustic gap is
now the main bottleneck. Augmentation shrinks it WITHOUT new data: we degrade
studio clips to resemble phone recordings, so the model learns to read stressed
prosody through codec artefacts and noise.

Applied ONLY to TRAINING clips, BEFORE the shared preprocessing (so the degraded
audio then goes through the exact same conditioning a real phone clip would).
Validation/test clips are never augmented - we evaluate on clean audio.
"""

import io

import numpy as np
from scipy.signal import butter, sosfilt


def add_background_noise(audio: np.ndarray, snr_db: float, rng) -> np.ndarray:
    """Add white noise at a target signal-to-noise ratio (dB)."""
    sig_power = float(np.mean(audio ** 2)) + 1e-10
    noise = rng.randn(len(audio)).astype(np.float32)
    noise_power = float(np.mean(noise ** 2)) + 1e-10
    scale = np.sqrt(sig_power / (noise_power * 10 ** (snr_db / 10.0)))
    return (audio + scale * noise).astype(np.float32)


def apply_reverb(audio: np.ndarray, sr: int, rng) -> np.ndarray:
    """Convolve with a short exponentially-decaying noise impulse response -
    a cheap room reverb (real rooms, not a studio booth)."""
    ir_len = max(8, int(sr * rng.uniform(0.08, 0.30)))
    ir = rng.randn(ir_len).astype(np.float32) * np.exp(-np.linspace(0, 6, ir_len))
    ir[0] = 1.0  # keep the direct path
    wet = np.convolve(audio, ir)[: len(audio)]
    peak = np.max(np.abs(wet)) + 1e-9
    wet = wet / peak * (np.max(np.abs(audio)) + 1e-9)
    return (0.7 * audio + 0.3 * wet).astype(np.float32)


def telephone_bandpass(audio: np.ndarray, sr: int) -> np.ndarray:
    """Band-limit to ~300-3400 Hz - the classic narrow phone bandwidth."""
    lo, hi = 300.0 / (sr / 2), min(3400.0 / (sr / 2), 0.99)
    sos = butter(4, [lo, hi], btype="band", output="sos")
    return sosfilt(sos, audio).astype(np.float32)


def codec_roundtrip(audio: np.ndarray, sr: int) -> np.ndarray:
    """Encode->decode through lossy OGG Vorbis to mimic WhatsApp/phone codec
    artefacts. No-op if libsndfile lacks OGG support (stays safe)."""
    try:
        import soundfile as sf
        buf = io.BytesIO()
        sf.write(buf, audio, sr, format="OGG", subtype="VORBIS")
        buf.seek(0)
        y, _ = sf.read(buf, dtype="float32")
        return np.asarray(y, dtype=np.float32)
    except Exception:
        return audio


def augment(audio: np.ndarray, sr: int, rng=None) -> np.ndarray:
    """Apply a RANDOM subset of phone-domain degradations to one clip.
    Deterministic given `rng`, so a run is reproducible."""
    rng = rng or np.random.RandomState()
    x = np.asarray(audio, dtype=np.float32).copy()
    if x.size == 0:
        return x
    if rng.rand() < 0.7:
        x = add_background_noise(x, snr_db=rng.uniform(8.0, 25.0), rng=rng)
    if rng.rand() < 0.5:
        x = apply_reverb(x, sr, rng)
    if rng.rand() < 0.6:
        x = codec_roundtrip(x, sr)
    if rng.rand() < 0.4:
        x = telephone_bandpass(x, sr)
    # guard against any overflow the chain introduced
    peak = np.max(np.abs(x)) + 1e-9
    if peak > 1.0:
        x = x / peak
    return x.astype(np.float32)
