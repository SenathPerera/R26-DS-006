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
    # A quiet room with NO detected voice must pass the ambient check.
    _patch_vad(monkeypatch, lambda audio, sr: [])
    rng = np.random.RandomState(0)
    quiet = (0.008 * rng.randn(SAMPLE_RATE * 3)).astype(np.float32)
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
