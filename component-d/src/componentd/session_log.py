"""Session logging - persist each live check-in so a test run yields reusable,
labelled data and can be audited afterwards.

Why this exists: the planned 4-person English+Sinhala live test is PROSPECTIVE
validation. If each subject's pre/post clips + model scores + their SELF-REPORTED
stress (the ground truth) + language are saved, the test extends the held-out
real-voice set (data/real_voice_eval, n=24) with new labelled pairs and becomes
analysable evidence for the dissertation/paper - instead of numbers that scroll
past once in a terminal and are gone.

Storage (all under data/session_logs/, gitignored like every dataset here):
  <session_id>/<phase>.wav   - the raw decoded input clip, exactly what was scored
  sessions.jsonl             - one JSON line per COMPLETED session

An in-memory buffer holds per-phase metadata between the two /infer calls and the
final /full-session call; the JSONL line is written once the session completes.
Nothing here changes scoring - it only records what happened, so it is safe to
leave off (the server passes log=False by default) or on.
"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import DATA_DIR, SAMPLE_RATE


class SessionLogger:
    """Persist live-session clips + a JSONL record. In-memory buffer for the
    per-phase notes; disk for clips and the final record. One instance per
    server process (mirrors PersonalBaseline / the HRV stores)."""

    def __init__(self, root: Path | None = None):
        self.root = Path(root) if root is not None else DATA_DIR / "session_logs"
        self.root.mkdir(parents=True, exist_ok=True)
        self.jsonl = self.root / "sessions.jsonl"
        # session_id -> {phase -> note dict}
        self.pending: dict[str, dict] = {}

    def save_clip(self, session_id: str, phase: str,
                  audio: np.ndarray, sr: int = SAMPLE_RATE) -> str:
        """Write one phase's raw clip to <root>/<session_id>/<phase>.wav.
        Returns the path (str). Audio is the decoded input, before conditioning,
        so the clip can be re-scored later with any checkpoint."""
        d = self.root / session_id
        d.mkdir(parents=True, exist_ok=True)
        path = d / f"{phase}.wav"
        sf.write(str(path), np.asarray(audio, dtype=np.float32), sr)
        return str(path)

    def note(self, session_id: str, phase: str, meta: dict) -> None:
        """Buffer per-phase metadata (scores, language, warnings, clip path)
        until the session completes."""
        self.pending.setdefault(session_id, {})[phase] = dict(meta)

    def complete(self, session_id: str, record: dict) -> dict:
        """Merge the buffered per-phase notes with the final `record`
        (comparison, crossmodal, self-reported ground truth, ...), stamp a UTC
        time, append one JSON line, and drop the buffer. Returns the written
        record (also useful for the API response / tests)."""
        full = {
            "session_id": session_id,
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "phases": self.pending.pop(session_id, {}),
            **record,
        }
        with self.jsonl.open("a") as f:
            f.write(json.dumps(full) + "\n")
        return full
