"""Server-side speech-to-text for the health companion (the STT stage of the
voice pipeline STT -> LLM -> TTS).

Wraps faster-whisper (base model, CPU, int8). It transcribes the SAME 16 kHz
mono clip already uploaded for stress scoring, so one round trip does both jobs
and the phone never needs an on-device recognizer racing the microphone (which
is unreliable alongside AudioRecord on Android 10+).

DESIGN RULE - this module must NEVER raise. Component D has to keep working
(scoring, gating, the whole flow) even when faster-whisper isn't installed, the
model can't download, or a clip fails to transcribe. Every failure path returns
an empty transcript and logs it; the companion then falls back to a neutral turn
with no regression versus the pre-STT behaviour.

Note: Whisper's Sinhala ("si") support is markedly weaker than English. The
`language` hint is passed through, but callers should not over-promise Sinhala
accuracy in the UI.
"""

import logging
from collections import Counter

import numpy as np

log = logging.getLogger(__name__)


def _is_hallucination(text: str) -> bool:
    """True when a transcript is degenerate Whisper output (a repeated phrase
    loop, or near-zero lexical variety) rather than real speech.

    Whisper - especially the small `base` model on a low-resource language like
    Sinhala, or on long clips with pauses - loops a phrase ("I am a man of the
    same age as the man of the same age...") or emits phonetic mush. That text
    must never reach the companion, or it generates a reply from nonsense. We
    return an EMPTY transcript instead, so the companion falls back to a warm
    neutral turn.
    """
    words = text.lower().split()
    if len(words) < 8:
        return False                       # too short to judge - let it through
    unique_ratio = len(set(words)) / len(words)
    if unique_ratio < 0.35:                # heavy repetition across the whole clip
        return True
    # A single 4-word phrase repeating >=3 times is the classic loop signature.
    grams = [" ".join(words[i:i + 4]) for i in range(len(words) - 3)]
    if grams:
        _, top_n = Counter(grams).most_common(1)[0]
        if top_n >= 3:
            return True
    return False

# Lazy singletons, mirroring StressScorer._get_encoder() and
# layer1_quality._load_vad(): load once per process, on first use, so server
# startup stays fast and a missing/broken package never blocks import.
_MODEL = None
_LOAD_FAILED = False
_MODEL_SIZE = "base"


def _get_model():
    """Load the Whisper model once per process. Returns None (never raises) if
    faster-whisper is unavailable or the model cannot be built/downloaded."""
    global _MODEL, _LOAD_FAILED
    if _MODEL is not None:
        return _MODEL
    if _LOAD_FAILED:
        return None
    try:
        from faster_whisper import WhisperModel
        _MODEL = WhisperModel(_MODEL_SIZE, device="cpu", compute_type="int8")
        log.info("loaded faster-whisper model: %s (cpu/int8)", _MODEL_SIZE)
        return _MODEL
    except Exception as e:                       # noqa: BLE001 - must never raise
        _LOAD_FAILED = True
        log.warning("faster-whisper unavailable, STT disabled: %s", e)
        return None


def transcribe(audio: np.ndarray, language: str | None = None) -> dict:
    """Transcribe a 16 kHz mono float32 array already at SAMPLE_RATE.

    `language` is an optional Whisper hint ("en" / "si"); pass None to
    auto-detect. Returns {"text": str, "language": str | None}. Never raises -
    on any failure returns an empty transcript.
    """
    model = _get_model()
    if model is None or audio is None or getattr(audio, "size", 0) == 0:
        return {"text": "", "language": None}
    try:
        clip = np.ascontiguousarray(audio, dtype=np.float32)
        # beam_size=1 keeps a live turn responsive; the clip is already 16 kHz
        # mono float32 (read_audio guarantees this) so no resampling is needed.
        # Hallucination guards (BUG-A):
        #   condition_on_previous_text=False - stops the "repeat the last phrase"
        #     feedback loop that faster-whisper enables by default.
        #   vad_filter=True - drops silent stretches so pauses aren't transcribed
        #     into invented text (the main source of the garbage on long clips).
        #   no_speech_threshold - discards segments the model thinks are silence.
        #   temperature=0.0 - no sampling fallback that invents tokens.
        segments, info = model.transcribe(
            clip, language=language, beam_size=1,
            condition_on_previous_text=False, vad_filter=True,
            no_speech_threshold=0.6, temperature=0.0)
        text = " ".join(seg.text.strip() for seg in segments).strip()
        detected = getattr(info, "language", None)
        # Final net: even with the guards, `base` can loop on Sinhala / accented
        # speech. Reject degenerate output so the companion never replies to mush.
        if _is_hallucination(text):
            log.info("transcript rejected as hallucination (lang=%s): %.60s", detected, text)
            return {"text": "", "language": detected}
        return {"text": text, "language": detected}
    except Exception as e:                       # noqa: BLE001 - must never raise
        log.warning("transcription failed: %s", e)
        return {"text": "", "language": None}
