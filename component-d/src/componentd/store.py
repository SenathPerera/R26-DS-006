"""Durable storage for Component D (PROBLEM 6).

Layer 5 and the personal baseline can only ever activate once a user has 3–5
sessions of history — but that history lived in plain process memory and died on
every uvicorn restart, so it was unreachable in practice. This module persists
it to a small SQLite database (stdlib ``sqlite3`` — no new dependency) so the
history survives restarts and a mid-session restart no longer 404s /full-session.

Design notes:
  * One connection per call (SQLite is fine with this and it sidesteps FastAPI's
    threadpool touching a shared connection from many threads).
  * Fail-soft: if the DB can't be opened or a query throws, we log and carry on
    with in-memory behaviour rather than 500-ing a live check-in. ``store.ok``
    tells callers whether persistence is actually available.
  * The in-memory dicts in server/main.py stay as a hot cache IN FRONT of this.
"""

from __future__ import annotations

import json
import sqlite3
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import DATA_DIR

_SCHEMA = """
CREATE TABLE IF NOT EXISTS sessions (
    session_id      TEXT PRIMARY KEY,
    user_id         TEXT,
    language        TEXT,
    created_at      REAL,
    completed_at    REAL,
    verdict_json    TEXT,
    comparison_json TEXT,
    crossmodal_json TEXT,
    anomaly_json    TEXT,
    baseline_json   TEXT
);
CREATE TABLE IF NOT EXISTS phase_readings (
    session_id   TEXT,
    phase        TEXT,
    stress_score REAL,
    stress_level TEXT,
    stress_type  TEXT,
    confidence   REAL,
    valence      REAL,
    arousal      REAL,
    gate_mean    REAL,
    quality_json TEXT,
    warnings_json TEXT,
    transcript   TEXT,
    result_json  TEXT,
    created_at   REAL,
    PRIMARY KEY (session_id, phase)
);
CREATE TABLE IF NOT EXISTS baseline_history (
    user_id      TEXT,
    session_id   TEXT,
    stress_score REAL,
    created_at   REAL,
    PRIMARY KEY (user_id, session_id)
);
CREATE TABLE IF NOT EXISTS anomaly_history (
    user_id    TEXT,
    session_id TEXT,
    error      REAL,
    created_at REAL,
    PRIMARY KEY (user_id, session_id)
);
"""


class ComponentDStore:
    def __init__(self, db_path: Path | None = None):
        self.path = Path(db_path) if db_path is not None else DATA_DIR / "componentd.db"
        self.ok = False
        try:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with self._conn() as c:
                c.executescript(_SCHEMA)
            self.ok = True
        except Exception as e:  # noqa: BLE001 - fail soft, never crash startup
            print(f"[store] disabled (memory-only): {e}")

    def _conn(self) -> sqlite3.Connection:
        conn = sqlite3.connect(str(self.path), timeout=5.0)
        conn.row_factory = sqlite3.Row
        return conn

    # ---------------------------------------------------------- phase readings
    def save_phase_reading(self, session_id: str, phase: str, result: dict,
                           transcript: str | None = None) -> None:
        if not self.ok:
            return
        try:
            q = result.get("quality") or {}
            with self._conn() as c:
                c.execute(
                    """INSERT INTO phase_readings
                       (session_id, phase, stress_score, stress_level, stress_type,
                        confidence, valence, arousal, gate_mean, quality_json,
                        warnings_json, transcript, result_json, created_at)
                       VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)
                       ON CONFLICT(session_id, phase) DO UPDATE SET
                        stress_score=excluded.stress_score, stress_level=excluded.stress_level,
                        stress_type=excluded.stress_type, confidence=excluded.confidence,
                        valence=excluded.valence, arousal=excluded.arousal,
                        gate_mean=excluded.gate_mean, quality_json=excluded.quality_json,
                        warnings_json=excluded.warnings_json, transcript=excluded.transcript,
                        result_json=excluded.result_json, created_at=excluded.created_at""",
                    (session_id, phase, result.get("stress_score"),
                     result.get("stress_level"), result.get("stress_type"),
                     result.get("confidence"), result.get("valence"),
                     result.get("arousal"), result.get("gate_mean"),
                     json.dumps(q), json.dumps(result.get("warnings") or []),
                     transcript, json.dumps(result), time.time()))
        except Exception as e:  # noqa: BLE001
            print(f"[store] save_phase_reading failed: {e}")

    def get_phase_readings(self, session_id: str) -> dict:
        """Reassemble {phase: result} exactly as session_scores holds it."""
        if not self.ok:
            return {}
        try:
            with self._conn() as c:
                rows = c.execute(
                    "SELECT phase, result_json FROM phase_readings WHERE session_id=?",
                    (session_id,)).fetchall()
            return {r["phase"]: json.loads(r["result_json"]) for r in rows}
        except Exception as e:  # noqa: BLE001
            print(f"[store] get_phase_readings failed: {e}")
            return {}

    # ---------------------------------------------------------------- sessions
    def save_session(self, session_id: str, user_id: str, language: str | None,
                     verdict: dict | None, comparison: dict | None,
                     crossmodal: dict | None, anomaly: dict | None,
                     baseline: dict | None) -> None:
        if not self.ok:
            return
        try:
            now = time.time()
            with self._conn() as c:
                c.execute(
                    """INSERT INTO sessions
                       (session_id, user_id, language, created_at, completed_at,
                        verdict_json, comparison_json, crossmodal_json,
                        anomaly_json, baseline_json)
                       VALUES (?,?,?,?,?,?,?,?,?,?)
                       ON CONFLICT(session_id) DO UPDATE SET
                        user_id=excluded.user_id, language=excluded.language,
                        completed_at=excluded.completed_at,
                        verdict_json=excluded.verdict_json,
                        comparison_json=excluded.comparison_json,
                        crossmodal_json=excluded.crossmodal_json,
                        anomaly_json=excluded.anomaly_json,
                        baseline_json=excluded.baseline_json""",
                    (session_id, user_id, language, now, now,
                     json.dumps(verdict), json.dumps(comparison),
                     json.dumps(crossmodal), json.dumps(anomaly),
                     json.dumps(baseline)))
        except Exception as e:  # noqa: BLE001
            print(f"[store] save_session failed: {e}")

    def list_sessions(self, user_id: str, limit: int = 20) -> list[dict]:
        if not self.ok:
            return []
        try:
            with self._conn() as c:
                rows = c.execute(
                    """SELECT s.session_id, s.created_at, s.comparison_json, s.anomaly_json,
                              pre.stress_score AS pre_stress, post.stress_score AS post_stress
                       FROM sessions s
                       LEFT JOIN phase_readings pre
                         ON pre.session_id=s.session_id AND pre.phase='pre'
                       LEFT JOIN phase_readings post
                         ON post.session_id=s.session_id AND post.phase='post'
                       WHERE s.user_id=?
                       ORDER BY s.created_at DESC LIMIT ?""",
                    (user_id, limit)).fetchall()
            out = []
            for r in rows:
                comp = json.loads(r["comparison_json"] or "null") or {}
                anom = json.loads(r["anomaly_json"] or "null") or {}
                out.append({
                    "session_id": r["session_id"],
                    "created_at": r["created_at"],
                    "pre_stress": r["pre_stress"],
                    "post_stress": r["post_stress"],
                    "delta": comp.get("delta"),
                    "direction": comp.get("direction"),
                    "reliable": comp.get("reliable"),
                    "anomaly_flag": bool(anom.get("anomaly")) if anom else False,
                })
            return out
        except Exception as e:  # noqa: BLE001
            print(f"[store] list_sessions failed: {e}")
            return []

    def get_full_session(self, session_id: str) -> dict | None:
        if not self.ok:
            return None
        try:
            with self._conn() as c:
                s = c.execute("SELECT * FROM sessions WHERE session_id=?",
                              (session_id,)).fetchone()
            phases = self.get_phase_readings(session_id)
            if s is None and not phases:
                return None
            base = dict(s) if s is not None else {"session_id": session_id}
            return {
                "session_id": session_id,
                "user_id": base.get("user_id"),
                "language": base.get("language"),
                "created_at": base.get("created_at"),
                "phases": phases,
                "verdict": json.loads(base.get("verdict_json") or "null"),
                "comparison": json.loads(base.get("comparison_json") or "null"),
                "crossmodal": json.loads(base.get("crossmodal_json") or "null"),
                "anomaly": json.loads(base.get("anomaly_json") or "null"),
                "personal_baseline": json.loads(base.get("baseline_json") or "null"),
            }
        except Exception as e:  # noqa: BLE001
            print(f"[store] get_full_session failed: {e}")
            return None

    # ------------------------------------------------------- baseline history
    def observe_baseline(self, user_id: str, session_id: str,
                         stress_score: float) -> None:
        if not self.ok:
            return
        try:
            with self._conn() as c:
                c.execute(
                    """INSERT INTO baseline_history (user_id, session_id, stress_score, created_at)
                       VALUES (?,?,?,?)
                       ON CONFLICT(user_id, session_id) DO UPDATE SET
                        stress_score=excluded.stress_score""",
                    (user_id, session_id or f"anon-{time.time()}", float(stress_score), time.time()))
        except Exception as e:  # noqa: BLE001
            print(f"[store] observe_baseline failed: {e}")

    def load_baseline_history(self) -> dict[str, list[float]]:
        if not self.ok:
            return {}
        try:
            with self._conn() as c:
                rows = c.execute(
                    "SELECT user_id, stress_score FROM baseline_history ORDER BY created_at"
                ).fetchall()
            out: dict[str, list[float]] = {}
            for r in rows:
                out.setdefault(r["user_id"], []).append(float(r["stress_score"]))
            return out
        except Exception as e:  # noqa: BLE001
            print(f"[store] load_baseline_history failed: {e}")
            return {}

    # -------------------------------------------------------- anomaly history
    def observe_anomaly(self, user_id: str, session_id: str, error: float) -> None:
        if not self.ok:
            return
        try:
            with self._conn() as c:
                c.execute(
                    """INSERT INTO anomaly_history (user_id, session_id, error, created_at)
                       VALUES (?,?,?,?)
                       ON CONFLICT(user_id, session_id) DO UPDATE SET error=excluded.error""",
                    (user_id, session_id or f"anon-{time.time()}", float(error), time.time()))
        except Exception as e:  # noqa: BLE001
            print(f"[store] observe_anomaly failed: {e}")

    def load_anomaly_history(self) -> dict[str, list[float]]:
        if not self.ok:
            return {}
        try:
            with self._conn() as c:
                rows = c.execute(
                    "SELECT user_id, error FROM anomaly_history ORDER BY created_at"
                ).fetchall()
            out: dict[str, list[float]] = {}
            for r in rows:
                out.setdefault(r["user_id"], []).append(float(r["error"]))
            return out
        except Exception as e:  # noqa: BLE001
            print(f"[store] load_anomaly_history failed: {e}")
            return {}
