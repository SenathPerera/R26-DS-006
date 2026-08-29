"""Personal-baseline normalisation - turn an ABSOLUTE stress score into a
RELATIVE one: how stressed is this user compared to their OWN normal.

Why this exists: on real phone voices the model's absolute stress range is
compressed toward the middle (the acoustic domain gap - see docs/ABLATION).
But the product does not need a perfect absolute number; it needs CHANGE and
DEVIATION. Measuring each user against their own history sidesteps the
compression entirely - a stressed person still reads as "higher than their
usual" even if the absolute value only moves from 3.9 to 5.4. This mirrors the
within-baseline-normalisation approach Component B (HRV) uses, so the two
components reason about stress the same way.

Cold start is stated honestly: before a few sessions there is no personal
baseline yet, so no relative claim is made.
"""

import numpy as np

# Sessions of history before a personal baseline is trustworthy.
MIN_HISTORY = 3


class PersonalBaseline:
    """Per-user stress history -> a relative reading. In-memory here; a
    database would back `history` in production (same as Layers 4/5)."""

    def __init__(self, min_history: int = MIN_HISTORY, store=None):
        self.min_history = min_history
        self.history: dict[str, list[float]] = {}
        # Optional durable backing (componentd.store.ComponentDStore). When set,
        # observe() writes through so history survives a server restart — the fix
        # that finally lets MIN_HISTORY be reached across restarts (PROBLEM 6).
        self.store = store

    def load_history(self, store) -> None:
        """Reload all per-user history from a durable store at startup."""
        self.store = store
        try:
            self.history = store.load_baseline_history()
        except Exception as e:  # noqa: BLE001 - fail soft to memory-only
            print(f"[baseline] load_history failed: {e}")

    def observe(self, user_id: str, stress_score: float,
                session_id: str | None = None) -> None:
        """Record a reading into this user's baseline history (and the store)."""
        self.history.setdefault(user_id, []).append(float(stress_score))
        if self.store is not None:
            self.store.observe_baseline(user_id, session_id or "", float(stress_score))

    def relative(self, user_id: str, stress_score: float) -> dict:
        """Compare `stress_score` to the user's own normal. Call this BEFORE
        observe() so the current reading is not part of its own baseline.

        Cold start (< min_history sessions) -> personalised=False, no claim."""
        hist = self.history.get(user_id, [])
        if len(hist) < self.min_history:
            return {
                "personalised": False,
                "baseline": None,
                "deviation": None,
                "z": None,
                "relative_band": None,
                "note": f"learning your baseline "
                        f"({len(hist)}/{self.min_history} sessions)",
            }
        arr = np.asarray(hist, dtype=float)
        mean = float(arr.mean())
        # Floor the spread so a user with a very steady history isn't flagged
        # for a trivial fluctuation - 0.5 (on 0-10) is the smallest stress
        # change we treat as meaningful.
        std = max(float(arr.std()), 0.5)
        z = (float(stress_score) - mean) / std
        if z < -0.5:
            band = "below your usual"
        elif z <= 0.5:
            band = "typical for you"
        elif z <= 1.5:
            band = "above your usual"
        else:
            band = "much higher than usual"
        return {
            "personalised": True,
            "baseline": round(mean, 2),
            "deviation": round(float(stress_score) - mean, 2),
            "z": round(z, 2),
            "relative_band": band,
        }
