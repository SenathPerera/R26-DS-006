"""Component D API server - the single entry point for every client.

The mobile app / VR headset / teammates' components only ever talk to
these endpoints. All five layers are wired here. Missing model
checkpoints disable their endpoint with a clean 503 (never a crash),
so the system runs even before training is complete.

Run (you will do this manually):
  .venv/bin/uvicorn server.main:app --host 0.0.0.0 --port 8010

Port 8010, not 8000: 8000 is a common clash point with other local dev
servers, and on macOS a process bound specifically to 127.0.0.1 silently
wins loopback traffic over a 0.0.0.0 bind - so a clash there fails
invisibly (a different app answers) instead of a loud "port in use"
error. This exact thing happened during development of this component.
"""

import io
import math
import numbers
import os
import shutil
import subprocess
import tempfile
import uuid
from contextlib import asynccontextmanager
from pathlib import Path

import librosa
import numpy as np
import soundfile as sf
from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response
from pydantic import BaseModel

# This file lives at <repo>/server/main.py; put <repo>/src on the path so the
# `componentd` package imports regardless of the cwd uvicorn is launched from.
import sys
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "src"))

from componentd.config import (MODELS_DIR, SAMPLE_RATE, STRESS_LEVELS,
                                faint_confidence_penalty)
from componentd.layer1_quality import check_ambient, check_speech
from componentd.layer3_compare import compare_scores
from componentd.layer4_crossmodal import (MockHRVProvider, StoredHRVProvider,
                                   normalize_level, validate_crossmodal,
                                   validate_crossmodal_levels)
from componentd.component_b_client import poll_into_store
from componentd.layer5_anomaly import SessionAnomalyDetector
from componentd.personal_baseline import PersonalBaseline
from componentd.companion import HealthCompanion, is_crisis
from componentd.companion.transcribe import transcribe
from componentd.session_log import SessionLogger
from componentd.store import ComponentDStore

# Trained checkpoints (produced by the training scripts). The fusion model
# defaults to fusion_meld_baseline: under valence-primary scoring it gives the
# best real-voice separation (LOO 92%, Sinhala 91% - see docs/ABLATION_STUDY.md),
# because it trained on natural (MELD) speech so its valence generalises. Override
# with FUSION_CKPT=<name> (checkpoint stem in models/) for experiments.
FUSION_CKPT = MODELS_DIR / f"{os.environ.get('FUSION_CKPT', 'fusion_meld_baseline')}.pt"
ANOMALY_CKPT = MODELS_DIR / "anomaly_v2.pt"

# Populated at startup if the checkpoints exist.
scorer = None
anomaly_detector = None

# HRV pushed by Component B lives here; the mock serves solo demos.
hrv_store = StoredHRVProvider()
hrv_mock = MockHRVProvider()

# Durable storage (SQLite). Fail-soft: if it can't open, everything below falls
# back to memory-only. In-memory dicts stay as a hot cache in front of it.
store = ComponentDStore()

# Per-user stress history for relative ("vs your own normal") reporting.
# Backed by the store so history survives restarts (PROBLEM 6).
personal_baseline = PersonalBaseline()

# The AI voice health companion (LLM dialogue stage). Stress-aware: the app
# injects Component D's voice stress and Component B's HRV level as a private
# note (no tool-calling - small local models are unreliable at it).
companion = HealthCompanion(
    get_voice=lambda sid, phase: _scores_for(sid).get(phase),
    get_body=lambda sid, phase: hrv_store.get_level(sid, phase),
)

# Per-session Layer 2 results, so /compare and /full-session can look back.
# Hot cache in front of the SQLite store; a restart between pre and post no
# longer 404s /full-session because _scores_for() reloads from disk on a miss.
session_scores: dict[str, dict] = {}


def _scores_for(session_id: str) -> dict:
    """Layer 2 results for a session: cache first, else reload from the store
    (survives a restart between the pre and post recordings)."""
    if session_id in session_scores:
        return session_scores[session_id]
    stored = store.get_phase_readings(session_id)
    if stored:
        session_scores[session_id] = stored
    return stored

# Optional persistence of live sessions (clips + scores + self-reported ground
# truth + language) so a test run becomes reusable, labelled data. Off unless a
# request opts in via log=True - scoring is unaffected either way.
session_logger = SessionLogger()


@asynccontextmanager
async def lifespan(_: FastAPI):
    """Load whatever trained models exist, once, at server start."""
    global scorer, anomaly_detector
    # Reload per-user baseline history from disk so MIN_HISTORY carries across
    # restarts (PROBLEM 6). Fail-soft if the store is unavailable.
    personal_baseline.load_history(store)
    if store.ok:
        print(f"loaded personal baseline history for {len(personal_baseline.history)} user(s)")
    if FUSION_CKPT.exists():
        from componentd.layer2_inference import StressScorer
        scorer = StressScorer(str(FUSION_CKPT))
        print(f"loaded fusion model: {FUSION_CKPT}")
    else:
        print(f"fusion checkpoint missing ({FUSION_CKPT}) - /infer disabled")
    if ANOMALY_CKPT.exists():
        anomaly_detector = SessionAnomalyDetector(str(ANOMALY_CKPT), store=store)
        print(f"loaded anomaly model: {ANOMALY_CKPT}")
    else:
        print(f"anomaly checkpoint missing ({ANOMALY_CKPT}) - "
              f"/anomaly-check disabled")
    yield


app = FastAPI(title="CogniVoice Component D", version="2.0",
              lifespan=lifespan)

# The frontend (Vite dev server) and this API run on different ports,
# which browsers treat as different origins - without this, every fetch
# from the UI fails silently with a CORS error, even though curl/Postman
# work fine (they don't enforce CORS). Wide open here because this is a
# research demo on localhost; a real deployment should replace "*" with
# the mobile app's / hosted demo site's exact origin(s).
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


async def read_audio(file: UploadFile) -> np.ndarray:
    """Decode any uploaded audio to 16 kHz mono float32.

    Two-stage decode: soundfile is fast but only handles WAV/FLAC/OGG,
    so compressed uploads (MP3, M4A, WebM/Opus - which the UI offers and
    which browser MediaRecorder often produces) fall back to librosa,
    which decodes them via ffmpeg. Without this fallback every non-WAV
    upload failed with 'could not decode audio file'.
    """
    raw = await file.read()
    # fast path: soundfile straight from memory (WAV/FLAC/OGG, and MP3 on
    # recent libsndfile). Container formats (MP4/M4A/AAC, WebM/Opus) are not
    # supported by libsndfile and fall through to the ffmpeg path below.
    try:
        audio, sr = sf.read(io.BytesIO(raw), dtype="float32")
        if audio.ndim > 1:
            audio = audio.mean(axis=1)      # stereo -> mono
        if sr != SAMPLE_RATE:
            audio = librosa.resample(audio, orig_sr=sr, target_sr=SAMPLE_RATE)
        return audio
    except Exception:
        pass  # not a soundfile-supported format; try the ffmpeg path

    # fallback: shell out to ffmpeg, which decodes essentially anything (MP4/
    # M4A/AAC, WebM/Opus, MP3, ...) to 16 kHz mono WAV on stdout. We write the
    # upload to a temp FILE and give ffmpeg a seekable path (-i <path>) rather
    # than piping it via stdin (pipe:0): MP4/MOV containers routinely keep their
    # `moov` index at the END of the file, and ffmpeg cannot reach it over a
    # non-seekable pipe, so piped mp4s decode to empty audio (they play fine in
    # a seekable player). A real file is seekable and decodes reliably. We call
    # ffmpeg DIRECTLY rather than via librosa, because librosa only reaches
    # ffmpeg through the optional `audioread` package, not a dependency here.
    ffmpeg = shutil.which("ffmpeg")
    if ffmpeg is None:
        raise HTTPException(
            400, f"could not decode audio file ({file.filename}): "
                 "this format needs ffmpeg, which is not installed. Upload a "
                 "WAV/FLAC/OGG file, or install ffmpeg.")
    suffix = Path(file.filename or "").suffix or ".bin"
    with tempfile.NamedTemporaryFile(suffix=suffix) as tmp:
        tmp.write(raw)
        tmp.flush()
        try:
            proc = subprocess.run(
                [ffmpeg, "-nostdin", "-loglevel", "error", "-i", tmp.name,
                 "-ac", "1", "-ar", str(SAMPLE_RATE), "-f", "wav", "pipe:1"],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
        except subprocess.CalledProcessError as e:
            detail = (e.stderr or b"").decode("utf-8", "ignore").strip().splitlines()
            why = detail[-1] if detail else "unknown decode error"
            raise HTTPException(
                400, f"could not decode audio file ({file.filename}): {why}")
    audio, _ = sf.read(io.BytesIO(proc.stdout), dtype="float32")
    return audio if audio.ndim == 1 else audio.mean(axis=1)


MIN_AUDIO_SAMPLES = int(SAMPLE_RATE * 0.15)   # ~150 ms; shorter = empty/failed capture


def _require_nonempty(audio) -> None:
    """A failed/empty recording decodes to ~0 samples, whose metrics come out
    NaN (mean of nothing) and then crash JSON serialisation with a 500 the
    browser only sees as 'Failed to fetch'. Reject it cleanly instead."""
    if audio is None or getattr(audio, "size", 0) < MIN_AUDIO_SAMPLES:
        raise HTTPException(400, detail={"error": "no_audio",
            "reasons": ["No audio was captured — please record again, a little closer to the mic."]})


def _json_safe(obj):
    """Recursively replace non-finite floats (NaN/inf) with 0.0 so a degenerate
    metric can never 500 the endpoint. Also coerces numpy scalars to native."""
    if isinstance(obj, bool):
        return obj
    if isinstance(obj, numbers.Integral):
        return int(obj)
    if isinstance(obj, numbers.Real):
        f = float(obj)
        return f if math.isfinite(f) else 0.0
    if isinstance(obj, dict):
        return {k: _json_safe(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [_json_safe(v) for v in obj]
    return obj


# ------------------------------------------------------------ health
@app.get("/health")
def health():
    return {
        "status": "ok",
        "layers": {
            "layer1_quality": True,
            "layer2_fusion": scorer is not None,
            "layer3_compare": True,
            "layer4_crossmodal": True,
            "layer5_anomaly": anomaly_detector is not None,
        },
    }


@app.post("/warmup")
def warmup():
    """Preload the heavy models (the emotion2vec voice encoder and the STT model)
    so the FIRST real analysis isn't a multi-second cold start. The app fires
    this the moment the check-in opens, before the user has spoken. Idempotent
    and never fails the caller."""
    warmed = {"scorer": False, "stt": False}
    try:
        if scorer is not None:
            scorer.score_array(np.zeros(16000, dtype=np.float32))  # 1s silence forces the encoder to load
            warmed["scorer"] = True
    except Exception:                                # noqa: BLE001 - warmup must never fail
        pass
    try:
        warmed["stt"] = bool(transcribe(np.zeros(16000, dtype=np.float32)) is not None)
    except Exception:                                # noqa: BLE001
        pass
    return {"warmed": warmed}


# ----------------------------------------------------------- layer 1
@app.post("/ambient-check")
async def ambient_check(file: UploadFile = File(...)):
    """The 'stay silent' step: room must be quiet AND free of speech
    (checked via VAD, not the loudness-comparison heuristic /infer uses -
    see src/layer1_quality.py for why these must be different checks)."""
    audio = await read_audio(file)
    _require_nonempty(audio)
    return _json_safe(check_ambient(audio))


# ----------------------------------------------------------- layer 2
@app.post("/infer")
async def infer(file: UploadFile = File(...),
                session_id: str | None = None, phase: str = "pre",
                poll_b: bool = False, log: bool = False,
                user_id: str = "default", language: str | None = None):
    """Audio -> stress score. Layer 1 gates the input first.

    poll_b: pull Component B's live HRV reading (GET /stress/latest) AT THIS
    moment and store it for `phase`. Polling here - not at /full-session - keeps
    the body reading time-aligned with the voice: the pre body signal is captured
    when the user speaks pre, the post signal when they speak post. If B is not
    ready (503) or unreachable the poll returns nothing and Layer 4 later falls
    back to voice-only. The captured level is echoed as `body` for visibility."""
    if scorer is None:
        raise HTTPException(503, "fusion model not trained yet")
    audio = await read_audio(file)
    _require_nonempty(audio)

    quality = check_speech(audio)
    if not quality["ok"]:
        # 422 tells the app: recording unusable, ask the user to retry.
        raise HTTPException(422, detail={"error": "audio rejected by layer 1",
                                         "reasons": quality["reasons"]})

    result = scorer.score_array(audio)
    result["quality"] = quality["metrics"]
    result = _json_safe(result)          # sanitise BEFORE storing so downstream layers stay finite

    # Faint-recording guard: a clip quieter than FAINT_INPUT_RMS gets amplified
    # heavily by loudness normalisation, which can produce a CONFIDENT wrong read
    # (Phase-3 OOD failure). Down-weight confidence in proportion to how faint the
    # raw input was, and flag it - Layer 3 then widens its noise band and Layer 4
    # defers to Component B, instead of over-asserting on an amplified whisper.
    penalty = faint_confidence_penalty(quality["metrics"].get("rms", 1.0))
    if penalty < 1.0:
        result["confidence"] = round(result["confidence"] * penalty, 3)
        result["input_level"] = "faint"
        result.setdefault("warnings", []).append("faint_recording")

    # Remember this score so /compare and /full-session can use it.
    sid = session_id or str(uuid.uuid4())
    session_scores.setdefault(sid, {})[phase] = result
    store.save_phase_reading(sid, phase, result)   # survive a restart mid-session
    result["session_id"] = sid

    if poll_b:
        reading = poll_into_store(hrv_store, sid, phase)
        result["body"] = None if reading is None else {
            "level": reading.level, "confidence": reading.confidence,
            "source": "component_b"}

    # Optional: persist this clip + its scores so the session can be re-analysed
    # and the live test yields labelled data (ground truth is added at /full-session).
    if log:
        clip_path = session_logger.save_clip(sid, phase, audio)
        session_logger.note(sid, phase, {
            "user_id": user_id, "language": language, "clip": clip_path,
            "stress_score": result["stress_score"],
            "stress_level": result.get("stress_level"),
            "confidence": result["confidence"],
            "valence": result.get("valence"), "arousal": result.get("arousal"),
            "input_level": result.get("input_level", "ok"),
            "warnings": result.get("warnings", []),
            "body": result.get("body"),
        })
    return result


# ----------------------------------------------------------- layer 3
class CompareRequest(BaseModel):
    session_id: str


@app.post("/compare")
def compare(req: CompareRequest):
    stored = _scores_for(req.session_id)
    if "pre" not in stored or "post" not in stored:
        raise HTTPException(404, "need both pre and post /infer results "
                                 "for this session")
    return compare_scores(stored["pre"], stored["post"])


# ----------------------------------------------------------- layer 4
class SessionUpdate(BaseModel):
    """The contract with Component B: they POST this after each phase.

    Preferred: B sends its OWN prediction from the WESAD model, mirroring its
    `format_output`:
      - a POINT level in `stress_level` (its classes are relaxed/mild/moderate/
        high; "relaxed" is normalised to Component D's "no"), or
      - when uncertain (top-2 classes close), a two-element `band`, e.g.
        ["mild","moderate"] - D takes the higher level (conservative for a
        wellness app) and marks confidence LOW so Layer 4 defers rather than
        asserting a mismatch.
    `confidence` (0-1) is B's own certainty. Raw `rmssd` is also accepted (demo,
    or when only HRV is available)."""
    session_id: str
    phase: str                          # "pre" | "post"
    stress_level: str | None = None     # relaxed/no/mild/moderate/high
    band: list[str] | None = None       # uncertain: [low_level, high_level]
    confidence: float | None = None     # B's own confidence 0-1
    rmssd: float | None = None          # milliseconds (optional)


@app.post("/session-update")
def session_update(update: SessionUpdate):
    if update.phase not in ("pre", "post"):
        raise HTTPException(400, "phase must be 'pre' or 'post'")

    if update.band is not None:
        # Uncertain reading: take the HIGHER level (don't under-call stress in a
        # wellness context) and force LOW confidence so Layer 4 does not assert a
        # mismatch on it. Empty/oversized bands are rejected.
        levels = [normalize_level(x) for x in update.band]
        if not levels or any(lv not in STRESS_LEVELS for lv in levels):
            raise HTTPException(400, f"band levels must be within {STRESS_LEVELS}")
        higher = max(levels, key=STRESS_LEVELS.index)
        conf = update.confidence if update.confidence is not None else 0.2
        hrv_store.push_level(update.session_id, update.phase, higher, conf)
    elif update.stress_level is not None:
        level = normalize_level(update.stress_level)
        if level not in STRESS_LEVELS:
            raise HTTPException(400, f"stress_level must map to one of {STRESS_LEVELS}")
        hrv_store.push_level(update.session_id, update.phase, level, update.confidence)
    elif update.rmssd is not None:
        hrv_store.push(update.session_id, update.phase, update.rmssd)
    else:
        raise HTTPException(400, "provide stress_level/band (preferred) or rmssd")
    return {"stored": True}


def _run_crossmodal(session_id: str, voice_pre: float, voice_post: float,
                    use_mock: bool):
    """Prefer Component B's ordinal stress level; fall back to raw RMSSD
    (stored or mock). Returns the crossmodal dict, or None if no body data.

    Body readings are captured EARLIER, per phase: either B pushed them via
    /session-update, or D polled B at each /infer (poll_b) - both land in
    hrv_store keyed by phase. This function only compares what is already stored.

    Threads BOTH sides' confidence into Layer 4: Component D's per-phase
    confidence (|valence|, from the stored /infer result) and Component B's
    per-phase confidence (if it sent one). Layer 4 uses these to defer to HRV
    when the voice is uncertain instead of asserting a false mismatch."""
    stored = _scores_for(session_id)
    voice_conf = (float(stored.get("pre", {}).get("confidence", 1.0)),
                  float(stored.get("post", {}).get("confidence", 1.0)))

    if not use_mock:
        lvl_pre = hrv_store.get_level(session_id, "pre")
        lvl_post = hrv_store.get_level(session_id, "post")
        if lvl_pre and lvl_post:
            bcp = hrv_store.get_level_confidence(session_id, "pre")
            bcpo = hrv_store.get_level_confidence(session_id, "post")
            body_conf = (bcp if bcp is not None else 1.0,
                         bcpo if bcpo is not None else 1.0)
            return validate_crossmodal_levels(voice_pre, voice_post,
                                              lvl_pre, lvl_post,
                                              voice_conf, body_conf)
    provider = hrv_mock if use_mock else hrv_store
    r_pre = provider.get_rmssd(session_id, "pre")
    r_post = provider.get_rmssd(session_id, "post")
    if r_pre is None or r_post is None:
        return None
    return validate_crossmodal(voice_pre, voice_post, r_pre, r_post, voice_conf)


class CrossValidateRequest(BaseModel):
    session_id: str
    use_mock_hrv: bool = False   # demos without Component B connected


@app.post("/cross-validate")
def cross_validate(req: CrossValidateRequest):
    stored = _scores_for(req.session_id)
    if "pre" not in stored or "post" not in stored:
        raise HTTPException(404, "need both pre and post /infer results "
                                 "for this session")

    result = _run_crossmodal(req.session_id, stored["pre"]["stress_score"],
                             stored["post"]["stress_score"], req.use_mock_hrv)
    if result is None:
        raise HTTPException(404, "no stress/HRV from Component B for this session; "
                                 "B must call /session-update, or set use_mock_hrv")
    return result


# ----------------------------------------------------------- layer 5
class AnomalyRequest(BaseModel):
    user_id: str
    features: list[float]   # ANOMALY_FEATURES order - see config.py


@app.post("/anomaly-check")
def anomaly_check(req: AnomalyRequest):
    if anomaly_detector is None:
        raise HTTPException(503, "anomaly model not trained yet")
    return anomaly_detector.check(req.user_id, np.asarray(req.features))


# ---------------------------------------------- health companion (voice AI)
class CompanionMessage(BaseModel):
    session_id: str
    text: str          # the student's utterance (from STT / typed for the demo)


@app.post("/companion/message")
def companion_message(req: CompanionMessage, phase: str = "pre"):
    """One turn of the pre/post check-in. The companion is stress-aware - the
    app injects this session's voice + HRV stress so it tunes its warmth. Reply
    text is sent to TTS by the client. Uses a local Ollama model by default -
    run `ollama serve` (and `ollama pull qwen2.5`)."""
    try:
        return {"reply": companion.reply(req.session_id, req.text, phase)}
    except Exception as e:
        raise HTTPException(503, f"companion unavailable "
                                 f"(is Ollama running? `ollama serve`): {e}")


# ---------------------------------------------- realistic companion voice (TTS)
# ElevenLabs is proxied here so the API key stays server-side (never on the phone).
# If no key is set, the endpoint 503s and the app falls back to on-device TTS.
_ELEVEN_KEY = os.environ.get("ELEVENLABS_API_KEY")
# Default to a free-tier PREMADE voice (Sarah — warm, reassuring). The premium
# "library" voices (e.g. Rachel 21m00...) 402 on free keys, so don't default to one.
_ELEVEN_VOICE = os.environ.get("ELEVENLABS_VOICE_ID", "EXAVITQu4vr4xnSDxMaL")
_ELEVEN_MODEL = os.environ.get("ELEVENLABS_MODEL", "eleven_turbo_v2_5")


@app.get("/companion/tts")
def companion_tts(text: str, language: str | None = None):
    """Speak a companion line in a realistic AI voice (ElevenLabs), returned as
    audio/mpeg for the phone to play. GET so the phone can stream it directly.

    Graceful by design: no key -> 503; upstream failure -> 503. The app treats a
    503 as "use on-device TTS instead", so the companion always speaks."""
    if not _ELEVEN_KEY:
        raise HTTPException(503, "tts not configured (set ELEVENLABS_API_KEY)")
    clean = (text or "").strip()
    if not clean:
        raise HTTPException(400, "empty text")
    # Multilingual model when Sinhala is requested; turbo (fast/cheap) for English.
    model = "eleven_multilingual_v2" if (language or "").lower().startswith(("si", "sin")) else _ELEVEN_MODEL
    try:
        import httpx
        r = httpx.post(
            f"https://api.elevenlabs.io/v1/text-to-speech/{_ELEVEN_VOICE}",
            headers={"xi-api-key": _ELEVEN_KEY, "accept": "audio/mpeg",
                     "content-type": "application/json"},
            json={"text": clean, "model_id": model,
                  "voice_settings": {"stability": 0.5, "similarity_boost": 0.75, "style": 0.0}},
            timeout=20.0,
        )
        r.raise_for_status()
        return Response(content=r.content, media_type="audio/mpeg")
    except Exception as e:                       # noqa: BLE001 - fall back to on-device TTS
        raise HTTPException(503, f"tts upstream failed: {e}")


# Whisper wants ISO codes; the app speaks in human language names.
_WHISPER_LANG = {"english": "en", "en": "en", "sinhala": "si", "si": "si"}

# When STT yields nothing (unavailable, or genuine silence) we still want a warm
# turn so the flow never dead-ends. A neutral placeholder nudges the companion to
# gently continue without polluting history with a fake utterance.
_EMPTY_TURN = "(the person is here but has not said much yet)"

# Warm canned lines used when the companion LLM (Ollama) is unreachable, so a
# down chat model never fails the turn — the transcript + scoring still return
# and the app can advance to the next phase / the report.
_FALLBACK_REPLY = {
    "pre": "Thank you for sharing that with me. Let's take a calm moment together next.",
    "post": "Thank you for telling me how you're feeling now. Let's look at how things shifted.",
}


@app.post("/companion/voice-turn")
async def companion_voice_turn(
    file: UploadFile = File(...),
    session_id: str | None = None, phase: str = "pre",
    user_id: str = "default", language: str | None = None,
    poll_b: bool = False, log: bool = False, is_final: bool = False):
    """One whole conversational turn in a single round trip: transcribe the clip,
    reply as the companion, and - only when is_final - score it exactly as /infer
    does and store it in session_scores so /full-session works unchanged.

    Additive: /infer and /companion/message are left exactly as they are. The
    phone posts the SAME WAV it would send to /infer; doing STT + dialogue +
    (optional) scoring here means the mic never races an on-device recognizer.

    is_final=False: transcribe + reply only (no scoring, no store) - lets the
    phone hold a multi-turn conversation and score once, on the concatenated clip.
    """
    sid = session_id or str(uuid.uuid4())
    audio = await read_audio(file)

    # Empty/failed capture: no scoring, but still a warm re-ask - never a dead end.
    try:
        _require_nonempty(audio)
    except HTTPException:
        return {"transcript": "", "reply": companion.reply(sid, _EMPTY_TURN, phase),
                "crisis": False, "accepted": False, "reasons": ["no_audio"],
                "quality": None, "analysis": None, "session_id": sid}

    stt = transcribe(audio, _WHISPER_LANG.get((language or "").lower()))
    transcript = stt["text"]

    quality = check_speech(audio)
    accepted = bool(quality["ok"])

    # Score only a final, accepted clip - and store it EXACTLY as /infer does, so
    # the private sensor note, /compare and /full-session all keep working with
    # zero downstream changes.
    analysis = None
    if accepted and is_final:
        if scorer is None:
            raise HTTPException(503, "fusion model not trained yet")
        result = scorer.score_array(audio)
        result["quality"] = quality["metrics"]
        result = _json_safe(result)
        penalty = faint_confidence_penalty(quality["metrics"].get("rms", 1.0))
        if penalty < 1.0:
            result["confidence"] = round(result["confidence"] * penalty, 3)
            result["input_level"] = "faint"
            result.setdefault("warnings", []).append("faint_recording")
        session_scores.setdefault(sid, {})[phase] = result
        store.save_phase_reading(sid, phase, result, transcript)  # survive a restart
        result["session_id"] = sid
        if poll_b:
            reading = poll_into_store(hrv_store, sid, phase)
            result["body"] = None if reading is None else {
                "level": reading.level, "confidence": reading.confidence,
                "source": "component_b"}
        analysis = result

    # Crisis net computed on the real transcript so the app can branch explicitly
    # instead of string-matching the reply. companion.reply() runs the same net
    # internally (returning CRISIS_REPLY), so the two always agree.
    crisis = is_crisis(transcript)
    try:
        reply = companion.reply(sid, transcript or _EMPTY_TURN, phase)
    except Exception:                       # noqa: BLE001 - LLM (Ollama) may be down
        # The turn's real value — the transcript and Layer-2 scoring — is already
        # computed and stored above, so a dead chat model must NOT fail the turn
        # (that would 500 and block the app from advancing to post / the report).
        reply = _FALLBACK_REPLY.get(phase, _FALLBACK_REPLY["pre"])

    if log:
        clip_path = session_logger.save_clip(sid, phase, audio)
        note = {"user_id": user_id, "language": language, "clip": clip_path,
                "transcript": transcript, "is_final": is_final,
                "accepted": accepted, "quality": quality["metrics"]}
        if analysis is not None:
            note.update(stress_score=analysis["stress_score"],
                        confidence=analysis["confidence"])
        session_logger.note(sid, phase, note)

    return {
        "transcript": transcript,
        "reply": reply,
        "crisis": crisis,
        "accepted": accepted,
        "reasons": quality["reasons"],
        "quality": quality["metrics"],
        "analysis": analysis,
        "session_id": sid,
    }


# ------------------------------------- full session (for Component C)
class FullSessionRequest(BaseModel):
    session_id: str
    user_id: str = "default"
    use_mock_hrv: bool = False
    # Live-test extras (all optional; scoring is unchanged if omitted):
    language: str | None = None            # "english" | "sinhala" - for the test log
    self_report_pre: float | None = None   # subject's own 0-10 stress before (ground truth)
    self_report_post: float | None = None  # subject's own 0-10 stress after
    notes: str | None = None               # free-text note for the session
    log: bool = False                      # persist a labelled record for later analysis


@app.post("/full-session")
def full_session(req: FullSessionRequest):
    """The one call the app makes after the post-session recording:
    comparison + cross-modal + anomaly, combined into the payload
    Component C (Unity) consumes."""
    stored = _scores_for(req.session_id)
    if "pre" not in stored or "post" not in stored:
        raise HTTPException(404, "need both pre and post /infer results "
                                 "for this session")

    comparison = compare_scores(stored["pre"], stored["post"])

    crossmodal = _run_crossmodal(req.session_id, stored["pre"]["stress_score"],
                                 stored["post"]["stress_score"], req.use_mock_hrv)

    anomaly = None
    if anomaly_detector is not None:
        # Session summary in ANOMALY_FEATURES order. Three values marked
        # "from app later" use defaults until the app supplies them.
        features = [
            stored["pre"]["stress_score"], stored["post"]["stress_score"],
            comparison["delta"],
            stored["pre"]["confidence"], stored["post"]["confidence"],
            15.0,                                          # session_duration
            crossmodal["agreement"] if crossmodal else 0.75,
            abs(stored["pre"].get("arousal", 0)
                - stored["post"].get("arousal", 0)),       # acoustic_variance
            stored["pre"].get("quality", {}).get("rms", 0.02),
            float(len(session_scores)),                    # session_number
            12.0,                                          # time_of_day
            1.0,                                           # days_since_last
        ]
        anomaly = anomaly_detector.check(req.user_id, np.asarray(features),
                                         session_id=req.session_id)

    # How the user's ARRIVAL (pre-session) stress compares to their own normal.
    # A relative reading that stays meaningful even when the absolute range is
    # compressed on real phone voices. Compute vs history, THEN record it.
    pre_stress = stored["pre"]["stress_score"]
    baseline = personal_baseline.relative(req.user_id, pre_stress)
    personal_baseline.observe(req.user_id, pre_stress, session_id=req.session_id)

    # Speaker-relative verdict: for an UNSEEN voice the absolute post score can be
    # confidently wrong (Phase-3), but the within-speaker pre->post CHANGE is
    # robust - so we name the change as the primary signal and mark the absolute
    # stress_level as secondary. Additive to the payload (Component C keeps reading
    # the existing fields; this only makes the honest ranking explicit).
    verdict = {
        "primary_signal": "change",
        "session_helped": comparison["improved"],
        "direction": comparison["direction"],
        "reliable": comparison["reliable"],
        "note": "Primary signal is the within-speaker pre->post change; the "
                "absolute stress_level is secondary and less reliable for an "
                "unseen voice.",
    }

    response = _json_safe({
        "stress_level": stored["post"]["stress_score"],
        "confidence": stored["post"]["confidence"],
        "verdict": verdict,
        "comparison": comparison,
        "crossmodal": crossmodal,
        "anomaly": anomaly,
        "personal_baseline": baseline,
    })

    if req.log:
        session_logger.complete(req.session_id, {
            "user_id": req.user_id,
            "language": req.language,
            "self_report_pre": req.self_report_pre,
            "self_report_post": req.self_report_post,
            "notes": req.notes,
            "verdict": verdict,
            "comparison": comparison,
            "crossmodal": crossmodal,
            "personal_baseline": baseline,
        })

    # Persist the completed session so it survives a restart and the history
    # endpoints / app can list it later (PROBLEM 6).
    store.save_session(req.session_id, req.user_id, req.language, verdict,
                       comparison, crossmodal, anomaly, baseline)
    return response


# ------------------------------------------------- session history (read)
@app.get("/sessions")
def list_sessions(user_id: str, limit: int = 20):
    """Recent completed sessions for a user, newest first — backs the app's
    Past-sessions screen. Empty list if the store is unavailable."""
    return store.list_sessions(user_id, limit)


@app.get("/session/{session_id}")
def get_session(session_id: str):
    """The full stored record for one session (both phases + all layers)."""
    rec = store.get_full_session(session_id)
    if rec is None:
        raise HTTPException(404, "no stored record for this session")
    return _json_safe(rec)
