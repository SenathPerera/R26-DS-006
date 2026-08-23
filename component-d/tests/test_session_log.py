"""Unit tests for the session logger (clips + JSONL record)."""

import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.session_log import SessionLogger


def test_save_clip_writes_wav(tmp_path):
    lg = SessionLogger(root=tmp_path)
    audio = (0.05 * np.random.RandomState(0).randn(16000)).astype(np.float32)
    p = Path(lg.save_clip("s1", "pre", audio))
    assert p.exists() and p.name == "pre.wav"
    assert p.parent.name == "s1"


def test_note_then_complete_writes_one_record(tmp_path):
    lg = SessionLogger(root=tmp_path)
    lg.note("s1", "pre", {"stress_score": 8.0, "clip": "x/pre.wav"})
    lg.note("s1", "post", {"stress_score": 3.0, "clip": "x/post.wav"})
    rec = lg.complete("s1", {"user_id": "p1", "language": "english",
                             "self_report_pre": 8.0})
    # returned record is complete + stamped
    assert rec["session_id"] == "s1" and "timestamp" in rec
    assert rec["language"] == "english" and rec["self_report_pre"] == 8.0
    assert rec["phases"]["pre"]["stress_score"] == 8.0
    assert rec["phases"]["post"]["clip"].endswith("post.wav")
    # one JSON line on disk
    lines = (tmp_path / "sessions.jsonl").read_text().strip().splitlines()
    assert len(lines) == 1 and json.loads(lines[0])["user_id"] == "p1"
    # buffer is cleared so a re-used session id starts fresh
    assert "s1" not in lg.pending


def test_multiple_sessions_append(tmp_path):
    lg = SessionLogger(root=tmp_path)
    for sid in ("a", "b", "c"):
        lg.note(sid, "pre", {"stress_score": 5.0})
        lg.complete(sid, {"user_id": sid})
    lines = (tmp_path / "sessions.jsonl").read_text().strip().splitlines()
    assert len(lines) == 3
    assert [json.loads(l)["session_id"] for l in lines] == ["a", "b", "c"]
