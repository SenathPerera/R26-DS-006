"""WP8 / PROBLEM 6: durable storage must let Layer 5 + the personal baseline
survive a server restart, and a mid-session restart must not 404 /full-session."""

import sys
from pathlib import Path

import numpy as np
from fastapi.testclient import TestClient

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.store import ComponentDStore
from componentd.personal_baseline import PersonalBaseline
from server import main as api_server


def _reading(score, conf=0.8, arousal=0.3):
    return {"stress_score": score, "stress_level": "moderate", "stress_type": "activated",
            "confidence": conf, "valence": -0.4, "arousal": arousal, "gate_mean": 0.5,
            "quality": {"rms": 0.02, "duration_sec": 8.0}}


def test_phase_readings_survive_restart(tmp_path):
    db = tmp_path / "c.db"
    s1 = ComponentDStore(db)
    s1.save_phase_reading("sess-1", "pre", _reading(7.0))
    s1.save_phase_reading("sess-1", "post", _reading(3.0))
    # A brand-new store against the same file = a server restart.
    s2 = ComponentDStore(db)
    scores = s2.get_phase_readings("sess-1")
    assert set(scores) == {"pre", "post"}
    assert scores["pre"]["stress_score"] == 7.0
    assert scores["post"]["stress_score"] == 3.0


def test_baseline_history_survives_restart_and_personalises(tmp_path):
    db = tmp_path / "c.db"
    s1 = ComponentDStore(db)
    pb = PersonalBaseline(store=s1)
    for i, v in enumerate([4.0, 5.0, 4.5]):
        pb.observe("u1", v, session_id=f"s{i}")
    # Restart: fresh store + fresh baseline reloading from the same db.
    s2 = ComponentDStore(db)
    pb2 = PersonalBaseline()
    pb2.load_history(s2)
    assert len(pb2.history["u1"]) == 3
    # With MIN_HISTORY reached across the "restart", it now personalises.
    assert pb2.relative("u1", 4.5)["personalised"] is True


def test_anomaly_history_survives_restart(tmp_path):
    db = tmp_path / "c.db"
    s1 = ComponentDStore(db)
    for i in range(5):
        s1.observe_anomaly("u1", f"s{i}", 0.1 + i * 0.01)
    s2 = ComponentDStore(db)
    hist = s2.load_anomaly_history()
    assert len(hist["u1"]) == 5


def test_list_and_get_session(tmp_path):
    db = tmp_path / "c.db"
    s = ComponentDStore(db)
    s.save_phase_reading("sess-9", "pre", _reading(6.0))
    s.save_phase_reading("sess-9", "post", _reading(2.0))
    s.save_session("sess-9", "u1", "english",
                   verdict={"direction": "improved"},
                   comparison={"delta": -4.0, "direction": "improved", "reliable": True},
                   crossmodal=None, anomaly={"anomaly": False}, baseline=None)
    rows = s.list_sessions("u1")
    assert len(rows) == 1 and rows[0]["session_id"] == "sess-9"
    assert rows[0]["delta"] == -4.0 and rows[0]["direction"] == "improved"
    full = s.get_full_session("sess-9")
    assert set(full["phases"]) == {"pre", "post"}
    assert full["comparison"]["delta"] == -4.0


def test_full_session_succeeds_after_restart(tmp_path, monkeypatch):
    """The 404 bug: a restart between pre and post empties the in-memory cache,
    but /full-session must still work by reloading phase readings from disk."""
    db = tmp_path / "c.db"
    fresh = ComponentDStore(db)
    monkeypatch.setattr(api_server, "store", fresh)
    fresh.save_phase_reading("sess-r", "pre", _reading(7.0, arousal=0.5))
    fresh.save_phase_reading("sess-r", "post", _reading(3.0, arousal=0.1))
    # Simulate the restart: the hot cache is empty.
    api_server.session_scores.pop("sess-r", None)

    client = TestClient(api_server.app)
    r = client.post("/full-session", json={"session_id": "sess-r", "user_id": "u1"})
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["comparison"]["delta"] == -4.0

    # And the completed session is now listable.
    rows = client.get("/sessions", params={"user_id": "u1"}).json()
    assert any(row["session_id"] == "sess-r" for row in rows)


def test_store_fail_soft_on_bad_path(tmp_path):
    # A directory where a file is expected -> store disables cleanly, no crash.
    bad = tmp_path / "adir"
    bad.mkdir()
    s = ComponentDStore(bad)
    assert s.ok is False
    assert s.get_phase_readings("x") == {}
    assert s.list_sessions("u") == []
    s.save_phase_reading("x", "pre", _reading(5.0))  # no exception
