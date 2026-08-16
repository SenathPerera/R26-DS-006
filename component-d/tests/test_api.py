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
