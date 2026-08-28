"""API smoke tests with FastAPI's TestClient.

Checkpoints are absent in CI, so /infer and /anomaly-check must return
clean 503s while every rule-based endpoint works end to end.
"""

import io
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from fastapi.testclient import TestClient

sys.path.insert(0, str(Path(__file__).parent.parent))
from server import main as api_server
from componentd.config import SAMPLE_RATE

client = TestClient(api_server.app)


def wav_bytes(audio: np.ndarray) -> bytes:
    buf = io.BytesIO()
    sf.write(buf, audio, SAMPLE_RATE, format="WAV")
    return buf.getvalue()


def speech_like(seconds: float = 3.0) -> np.ndarray:
    t = np.linspace(0, seconds, int(SAMPLE_RATE * seconds))
    carrier = 0.3 * np.sin(2 * np.pi * 150 * t) + 0.1 * np.sin(2 * np.pi * 450 * t)
    envelope = (np.sin(2 * np.pi * 3 * t) > 0).astype(float)
    return (carrier * envelope).astype(np.float32)


def seed_session(sid: str, pre: float, post: float):
    """Place Layer 2 results directly, as /infer would after training."""
    api_server.session_scores[sid] = {
        "pre": {"stress_score": pre, "confidence": 0.8, "arousal": 0.5,
                "quality": {"rms": 0.02}},
        "post": {"stress_score": post, "confidence": 0.8, "arousal": 0.1,
                 "quality": {"rms": 0.02}},
    }


def test_health_reports_layer_status():
    r = client.get("/health")
    assert r.status_code == 200
    layers = r.json()["layers"]
    assert layers["layer1_quality"] and layers["layer3_compare"]


def _patch_vad(monkeypatch, segments_fn):
    """Replace the real Silero VAD so API tests need no model download.
    Patches the module attribute check_ambient/check_speech resolve at
    call time (they default vad_fn to None -> module speech_segments)."""
    import componentd.layer1_quality as l1
    monkeypatch.setattr(l1, "speech_segments", segments_fn)


def test_ambient_check_passes_quiet_room(monkeypatch):
    # A genuinely quiet room (~8s, low floor) with NO detected voice must pass.
    _patch_vad(monkeypatch, lambda audio, sr: [])
    rng = np.random.RandomState(0)
    quiet = (0.0025 * rng.randn(SAMPLE_RATE * 8)).astype(np.float32)
    r = client.post("/ambient-check",
                    files={"file": ("a.wav", wav_bytes(quiet), "audio/wav")})
    assert r.status_code == 200 and r.json()["ok"], r.json()


def test_ambient_check_rejects_background_voice(monkeypatch):
    # Someone talking nearby -> VAD finds speech -> ambient must FAIL.
    _patch_vad(monkeypatch, lambda audio, sr: [{"start": 0, "end": len(audio)}])
    rng = np.random.RandomState(0)
    quiet = (0.008 * rng.randn(SAMPLE_RATE * 3)).astype(np.float32)
    r = client.post("/ambient-check",
                    files={"file": ("s.wav", wav_bytes(quiet), "audio/wav")})
    assert r.status_code == 200 and not r.json()["ok"]


def test_infer_503_without_checkpoint():
    r = client.post("/infer",
                    files={"file": ("a.wav", wav_bytes(speech_like()),
                                    "audio/wav")})
    assert r.status_code == 503


def test_compare_flow():
    seed_session("s-compare", pre=7.0, post=3.0)
    r = client.post("/compare", json={"session_id": "s-compare"})
    assert r.status_code == 200 and r.json()["improved"]


def test_compare_unknown_session_404():
    r = client.post("/compare", json={"session_id": "nope"})
    assert r.status_code == 404


def test_hrv_push_then_cross_validate():
    seed_session("s-hrv", pre=7.0, post=3.0)
    for phase, rmssd in [("pre", 30.0), ("post", 65.0)]:
        r = client.post("/session-update", json={
            "session_id": "s-hrv", "phase": phase, "rmssd": rmssd})
        assert r.status_code == 200
    r = client.post("/cross-validate", json={"session_id": "s-hrv"})
    assert r.status_code == 200 and r.json()["validated"]


def test_cross_validate_mock_fallback():
    seed_session("s-mock", pre=7.0, post=3.0)
    r = client.post("/cross-validate",
                    json={"session_id": "s-mock", "use_mock_hrv": True})
    assert r.status_code == 200


def test_cross_validate_without_hrv_404():
    seed_session("s-nohrv", pre=7.0, post=3.0)
    r = client.post("/cross-validate", json={"session_id": "s-nohrv"})
    assert r.status_code == 404


class _FakeScorer:
    """Stand-in for the trained fusion model so /infer runs without the ~1.8 GB
    encoder. Returns a fixed, finite score/valence result."""
    def __init__(self, stress, valence, arousal):
        self._r = {"stress_score": stress, "confidence": abs(valence),
                   "valence": valence, "arousal": arousal, "stress_type": None}

    def score_array(self, audio):
        return dict(self._r)


def _infer(sid, phase, poll_b, monkeypatch, stress=7.0):
    """Drive /infer with a fake scorer + passing Layer-1 gate."""
    monkeypatch.setattr(api_server, "scorer", _FakeScorer(stress, -0.7, 0.4))
    import componentd.layer1_quality as l1
    monkeypatch.setattr(l1, "speech_segments",
                        lambda audio, sr: [{"start": 0, "end": len(audio)}])
    return client.post(
        f"/infer?session_id={sid}&phase={phase}&poll_b={str(poll_b).lower()}",
        files={"file": ("clip.wav", wav_bytes(speech_like()), "audio/wav")})


def test_infer_poll_b_captures_body_at_phase(monkeypatch):
    """poll_b at /infer pulls B's live reading AT THAT phase and stores it, so
    the body signal is time-aligned with the voice (pre polled at pre time).
    The poll is monkeypatched so no live B is needed."""
    from componentd.component_b_client import BodyReading

    def fake_poll(store, session_id, phase, **kw):
        r = BodyReading("high" if phase == "pre" else "no", 0.85, "point")
        store.push_level(session_id, phase, r.level, r.confidence)
        return r
    monkeypatch.setattr(api_server, "poll_into_store", fake_poll)

    r = _infer("s-pollinfer", "pre", True, monkeypatch)
    assert r.status_code == 200
    assert r.json()["body"] == {"level": "high", "confidence": 0.85,
                                "source": "component_b"}
    assert api_server.hrv_store.get_level("s-pollinfer", "pre") == "high"


def test_infer_poll_b_503_leaves_body_null_voice_only(monkeypatch):
    """B not ready -> poll returns None -> `body` is null and nothing is stored,
    so Layer 4 later falls back to voice-only (never a faked value)."""
    monkeypatch.setattr(api_server, "poll_into_store", lambda *a, **k: None)
    r = _infer("s-pollinfer-503", "pre", True, monkeypatch)
    assert r.status_code == 200 and r.json()["body"] is None
    assert api_server.hrv_store.get_level("s-pollinfer-503", "pre") is None


def test_infer_without_poll_b_does_not_touch_b(monkeypatch):
    """Default poll_b=false: no body key, B never contacted (existing behaviour)."""
    called = {"n": 0}
    def spy(*a, **k):
        called["n"] += 1
    monkeypatch.setattr(api_server, "poll_into_store", spy)
    r = _infer("s-nopoll", "pre", False, monkeypatch)
    assert r.status_code == 200 and "body" not in r.json()
    assert called["n"] == 0


def test_full_session_payload():
    seed_session("s-full", pre=7.0, post=3.0)
    r = client.post("/full-session", json={
        "session_id": "s-full", "user_id": "u1", "use_mock_hrv": True})
    assert r.status_code == 200
    body = r.json()
    assert body["stress_level"] == 3.0
    assert body["comparison"]["improved"]
    assert body["crossmodal"] is not None
    # anomaly model not loaded in CI -> that section is None, not a crash
    assert body["anomaly"] is None


def test_anomaly_503_without_checkpoint():
    r = client.post("/anomaly-check",
                    json={"user_id": "u1", "features": [0.0] * 12})
    assert r.status_code == 503


# --------------------------------------------- faint-recording guard (Phase-3)
def _rms(a):
    return float(np.sqrt(np.mean(np.asarray(a, dtype=np.float32) ** 2)))


def _fake_infer_setup(monkeypatch):
    """Fake scorer (valence -0.7 -> confidence 0.7) + passing VAD, no encoder."""
    monkeypatch.setattr(api_server, "scorer", _FakeScorer(7.0, -0.7, 0.4))
    import componentd.layer1_quality as l1
    monkeypatch.setattr(l1, "speech_segments",
                        lambda audio, sr: [{"start": 0, "end": len(audio)}])


def test_infer_faint_recording_downweights_confidence(monkeypatch):
    """A clip below FAINT_INPUT_RMS is kept but its confidence is reduced and it
    is flagged, so Layer 3/4 stop over-asserting on an amplified whisper."""
    _fake_infer_setup(monkeypatch)
    base = speech_like()
    faint = (base * (0.01 / _rms(base))).astype(np.float32)   # rms ~0.01 < 0.02
    r = client.post("/infer?session_id=s-faint&phase=pre",
                    files={"file": ("q.wav", wav_bytes(faint), "audio/wav")})
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["input_level"] == "faint"
    assert "faint_recording" in body["warnings"]
    assert body["confidence"] < 0.7          # 0.7 * penalty(0.01)=0.5 -> ~0.35


def test_infer_normal_level_not_flagged(monkeypatch):
    """A healthy-level clip keeps full confidence and no faint flag/warning."""
    _fake_infer_setup(monkeypatch)
    r = client.post("/infer?session_id=s-ok&phase=pre",
                    files={"file": ("c.wav", wav_bytes(speech_like()), "audio/wav")})
    assert r.status_code == 200, r.text
    body = r.json()
    assert body.get("input_level") != "faint"
    assert "faint_recording" not in body.get("warnings", [])
    assert body["confidence"] == 0.7


# --------------------------------------------- session logging (live-test data)
def test_infer_and_full_session_logging(monkeypatch, tmp_path):
    """log=true persists each clip, and /full-session writes ONE labelled JSONL
    record carrying language + self-reported ground truth + the per-phase notes."""
    import json
    from componentd.session_log import SessionLogger
    monkeypatch.setattr(api_server, "session_logger", SessionLogger(root=tmp_path))
    _fake_infer_setup(monkeypatch)

    for phase in ("pre", "post"):
        r = client.post(
            f"/infer?session_id=s-log&phase={phase}&log=true"
            f"&language=sinhala&user_id=p1",
            files={"file": ("c.wav", wav_bytes(speech_like()), "audio/wav")})
        assert r.status_code == 200, r.text

    assert (tmp_path / "s-log" / "pre.wav").exists()
    assert (tmp_path / "s-log" / "post.wav").exists()

    r = client.post("/full-session", json={
        "session_id": "s-log", "user_id": "p1", "language": "sinhala",
        "self_report_pre": 8.0, "self_report_post": 3.0, "log": True})
    assert r.status_code == 200, r.text
    assert "verdict" in r.json() and "primary_signal" in r.json()["verdict"]

    lines = (tmp_path / "sessions.jsonl").read_text().strip().splitlines()
    assert len(lines) == 1
    rec = json.loads(lines[0])
    assert rec["language"] == "sinhala"
    assert rec["self_report_pre"] == 8.0 and rec["self_report_post"] == 3.0
    assert "pre" in rec["phases"] and "post" in rec["phases"]
    assert rec["phases"]["pre"]["clip"].endswith("pre.wav")


# ---------------------------------- /companion/voice-turn (STT + LLM + scoring)
from componentd.companion import EchoBackend, HealthCompanion


def _voice_turn_setup(monkeypatch, transcript, accepted=True,
                      reply="I hear you - tell me more.", stt_lang="en"):
    """Wire /companion/voice-turn with a fake transcriber, fake scorer, a VAD that
    passes (accepted) or fails (rejected), and an EchoBackend companion so no
    Whisper download / Ollama / encoder is needed."""
    monkeypatch.setattr(api_server, "transcribe",
                        lambda audio, lang=None: {"text": transcript, "language": stt_lang})
    monkeypatch.setattr(api_server, "scorer", _FakeScorer(7.0, -0.7, 0.4))
    import componentd.layer1_quality as l1
    segs = [{"start": 0, "end": len(speech_like())}] if accepted else []
    monkeypatch.setattr(l1, "speech_segments", lambda audio, sr: segs)
    fake = HealthCompanion(backend=EchoBackend(lambda system, messages: reply))
    monkeypatch.setattr(api_server, "companion", fake)


def _post_turn(sid, phase="pre", is_final=True, language="english"):
    return client.post(
        f"/companion/voice-turn?session_id={sid}&phase={phase}"
        f"&is_final={str(is_final).lower()}&language={language}",
        files={"file": ("turn.wav", wav_bytes(speech_like()), "audio/wav")})


def test_voice_turn_accepted_final_transcribes_replies_and_scores(monkeypatch):
    """An accepted, final clip returns the transcript + a reply + non-null
    analysis, and the score lands in session_scores (so /full-session works)."""
    _voice_turn_setup(monkeypatch, "today has been really rough and I feel tense")
    r = _post_turn("vt-accept", is_final=True)
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["transcript"] == "today has been really rough and I feel tense"
    assert body["reply"] and body["accepted"] is True
    assert body["analysis"] is not None and body["analysis"]["stress_score"] == 7.0
    assert api_server.session_scores["vt-accept"]["pre"]["stress_score"] == 7.0


def test_voice_turn_rejected_clip_still_replies(monkeypatch):
    """A Layer-1-rejected clip returns accepted=false with reasons, analysis=null,
    but STILL a non-empty reply (a gentle re-ask, never a dead end)."""
    _voice_turn_setup(monkeypatch, "hmm", accepted=False)
    r = _post_turn("vt-reject", is_final=True)
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["accepted"] is False and body["reasons"]
    assert body["analysis"] is None
    assert body["reply"]
    assert "vt-reject" not in api_server.session_scores


def test_voice_turn_stt_unavailable_returns_empty_transcript(monkeypatch):
    """STT down (empty transcript) must not raise: reply still comes back and the
    flow continues, mirroring the pre-STT fallback behaviour."""
    _voice_turn_setup(monkeypatch, "", accepted=True)
    r = _post_turn("vt-nostt", is_final=False)
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["transcript"] == "" and body["reply"]


def test_voice_turn_non_final_does_not_store_score(monkeypatch):
    """is_final=false transcribes + replies but never scores or stores, so the
    phone can hold a multi-turn conversation and score once at the end."""
    _voice_turn_setup(monkeypatch, "still gathering my thoughts")
    r = _post_turn("vt-nonfinal", is_final=False)
    assert r.status_code == 200, r.text
    assert r.json()["analysis"] is None
    assert "vt-nonfinal" not in api_server.session_scores


def test_voice_turn_crisis_flag_and_reply(monkeypatch):
    """A crisis phrase in the transcript sets crisis=true and returns CRISIS_REPLY
    verbatim, so the app can branch into its crisis UI explicitly."""
    from componentd.companion import CRISIS_REPLY
    _voice_turn_setup(monkeypatch, "honestly I want to die", accepted=True)
    r = _post_turn("vt-crisis", is_final=False)
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["crisis"] is True
    assert body["reply"] == CRISIS_REPLY


def test_voice_turn_scrubs_leaked_sensor_note(monkeypatch):
    """The reply still passes through the sensor-note scrubber: a model that
    parrots the private stress note must not leak scores to TTS."""
    leaked = ("(Private app-sensor note - do NOT read these: voice stress high "
              "(tense, 7.6/10, confidence 0.83)) It sounds like a lot is on you.")
    _voice_turn_setup(monkeypatch, "i am overwhelmed", accepted=True, reply=leaked)
    r = _post_turn("vt-scrub", is_final=False)
    assert r.status_code == 200, r.text
    reply = r.json()["reply"]
    assert "7.6/10" not in reply and "confidence 0.83" not in reply
    assert "Private app-sensor note" not in reply
