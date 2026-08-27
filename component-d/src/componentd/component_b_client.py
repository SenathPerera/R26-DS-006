"""Component B integration: poll B's HRV stress and feed it into Layer 4.

Component B runs its own continuous ~45 s HRV stress stream and exposes its
LATEST reading at GET {COMPONENT_B_URL}/stress/latest. Component D is episodic -
it only reads stress at two moments (the ~30 s pre and post speech clips). So D
POLLS B once per check-in, tags the reading with that phase, and hands it to the
existing Layer 4 cross-modal logic.

Boundary: this module OWNS the D-side poll + schema mapping only. It changes
nothing in Component B, and it does not re-implement Layer 4 - it feeds the same
StoredHRVProvider that the push endpoint (/session-update) already fills, so both
paths converge on identical cross-modal input.

Design split (so the mapping is testable without a network):
  * map_stress_prediction() - PURE: B's StressPrediction dict -> a BodyReading.
  * poll_latest()           - the thin HTTP call (200 -> map, 503/error -> None).
  * feed_into_store()       - stores a BodyReading via the shared push_level path.

B's wire schema (componentb/server/schemas/messages.py):
  top level: timestamp, heartRate, rmssd, sdnn, signalQuality, windowStart,
    windowEnd, and the gated decision NESTED under "stress".
  stress block: mode ("point"|"band"); level | level_low/level_high (int 0-3);
    label; confidence (0-1); adjacent; probabilities; continuous_score.
B's levels (componentb/config.CLASS_NAMES) are index-aligned with D's, so an int
level maps by B_CLASS_NAMES[level] then normalize_level() ("relaxed" -> "no").
"""

from __future__ import annotations

from dataclasses import dataclass

from componentd.config import (B_CLASS_NAMES, COMPONENT_B_TIMEOUT,
                               COMPONENT_B_URL, STRESS_LEVELS)
from componentd.layer4_crossmodal import StoredHRVProvider, normalize_level

# A band means B is UNCERTAIN (straddling two levels). Cap its confidence here so
# Layer 4's CONF_MIN (0.4) gate defers rather than asserting a mismatch off it -
# unless B's own reported confidence is already lower, in which case keep that.
# Mirrors the /session-update band rule (server/main.py) so both paths agree.
BAND_CONF_CAP = 0.2


@dataclass(frozen=True)
class BodyReading:
    """One phase's HRV stress from Component B, in Component D's vocabulary.

    `level` is one of STRESS_LEVELS ("no"/"mild"/"moderate"/"high"); for a band
    it is the HIGHER of the two (conservative - never under-call stress in a
    wellness context). `confidence` in [0,1] feeds Layer 4's defer gate.
    """
    level: str
    confidence: float
    mode: str            # "point" | "band" - kept for logging/debug
    label: str = ""      # B's own human label (e.g. "mild-to-moderate")


def _level_name(level_int: int) -> str:
    """B's integer level -> Component D level name, or raise if out of range.

    Never silently falls back to a mid-scale default: an unmapped level is a
    contract violation between B and D, not a 'moderate' reading."""
    if not isinstance(level_int, int) or isinstance(level_int, bool):
        raise ValueError(f"level must be an int, got {level_int!r}")
    if not 0 <= level_int < len(B_CLASS_NAMES):
        raise ValueError(f"level {level_int} outside B_CLASS_NAMES {B_CLASS_NAMES}")
    name = normalize_level(B_CLASS_NAMES[level_int])
    if name not in STRESS_LEVELS:
        raise ValueError(f"level {level_int} mapped to unknown '{name}'")
    return name


def map_stress_prediction(pred: dict) -> BodyReading:
    """PURE: turn one of B's StressPrediction dicts into a BodyReading.

    B nests the gated decision under a "stress" block (mode/level/label/
    confidence) alongside top-level physiology (heartRate/rmssd/sdnn). We read
    the decision from that block; a flat payload (decision at the top level) is
    still accepted, so if B tweaks the envelope again this degrades to a clean
    map rather than a silent voice-only fallback.

    Raises ValueError on a malformed payload (missing/oob level, bad mode) so a
    broken contract surfaces loudly instead of poisoning Layer 4 with a guess.
    """
    if not isinstance(pred, dict):
        raise ValueError(f"prediction must be a dict, got {type(pred).__name__}")
    block = pred.get("stress") if isinstance(pred.get("stress"), dict) else pred
    mode = block.get("mode")
    try:
        conf = float(block.get("confidence"))
    except (TypeError, ValueError):
        raise ValueError("prediction missing numeric 'confidence'")
    label = str(block.get("label", ""))

    if mode == "point":
        level = _level_name(block.get("level"))
        return BodyReading(level=level, confidence=conf, mode="point", label=label)

    if mode == "band":
        lo, hi = _level_name(block.get("level_low")), _level_name(block.get("level_high"))
        # Take the HIGHER level (don't under-call stress) and hold confidence LOW
        # so an uncertain B reading defers instead of asserting a mismatch.
        higher = max(lo, hi, key=STRESS_LEVELS.index)
        return BodyReading(level=higher, confidence=min(conf, BAND_CONF_CAP),
                           mode="band", label=label)

    raise ValueError(f"mode must be 'point' or 'band', got {mode!r}")


def poll_latest(session_id: str, phase: str, *,
                base_url: str | None = None,
                timeout: float | None = None,
                client=None) -> BodyReading | None:
    """Poll B's GET /stress/latest for the current check-in.

    Returns a BodyReading on success, or None when there is no usable body signal
    - i.e. B is not ready yet (HTTP 503, before its first ~45 s window) or is
    unreachable. None is the caller's cue to fall back to VOICE-ONLY; it is never
    faked into a stress value.

    `client` is an injectable httpx-like client (must expose .get(url, timeout));
    it exists so tests can drive 200/503/error paths without a live B. session_id
    and phase are accepted for symmetry/logging: B's latest reading is global to
    the current stream, so they are not sent as query params.
    """
    base = (base_url or COMPONENT_B_URL).rstrip("/")
    to = COMPONENT_B_TIMEOUT if timeout is None else timeout
    url = f"{base}/stress/latest"

    owns_client = client is None
    if owns_client:
        import httpx
        client = httpx.Client()
    try:
        resp = client.get(url, timeout=to)
    except Exception:
        # Unreachable / DNS / timeout -> no body signal, defer to voice-only.
        return None
    finally:
        if owns_client:
            client.close()

    if resp.status_code == 503:      # B has no full window yet
        return None
    if resp.status_code != 200:
        return None
    try:
        return map_stress_prediction(resp.json())
    except ValueError:
        # Malformed body from B: treat as no usable signal rather than crashing
        # a live session. (map_* still raises loudly for unit tests.)
        return None


def feed_into_store(store: StoredHRVProvider, session_id: str, phase: str,
                    reading: BodyReading) -> None:
    """Store a polled BodyReading exactly like the push endpoint does, so the
    poll path and B's /session-update path produce identical Layer 4 input."""
    store.push_level(session_id, phase, reading.level, reading.confidence)


def poll_into_store(store: StoredHRVProvider, session_id: str, phase: str,
                    **kwargs) -> BodyReading | None:
    """Convenience: poll B for this phase and, if a reading came back, store it.
    Returns the BodyReading (or None if B gave nothing -> voice-only fallback)."""
    reading = poll_latest(session_id, phase, **kwargs)
    if reading is not None:
        feed_into_store(store, session_id, phase, reading)
    return reading
