"""Causal multi-timescale baseline engine.

Causal by construction: the value at index i depends only on samples
up to i. Verified by corrupting future samples and confirming past
output is unchanged (see tests/test_causality.py).

Cosinor is NOT used at inference time — it fits the whole session,
which a live device cannot do.
"""

import numpy as np

from componentb.config import EWMA_HALFLIVES, POPULATION_RR_MS


class BaselineEngine:
    """Tracks a personal expected RR level at several timescales."""

    def __init__(self, population_level=POPULATION_RR_MS,
                 halflives=None):
        self.halflives = halflives or EWMA_HALFLIVES
        self.population_level = population_level
        # cold start: seed every scale from the population mean
        self.state = {k: float(population_level) for k in self.halflives}
        self.alpha = {
            k: 1 - np.exp(np.log(0.5) / max(hl, 1))
            for k, hl in self.halflives.items()
        }
        self.n_seen = 0

    def update(self, rr_ms):
        """Feed one new RR interval. Past-only, O(1)."""
        self.n_seen += 1
        for k, a in self.alpha.items():
            self.state[k] = a * rr_ms + (1 - a) * self.state[k]

    def update_many(self, rr_array):
        for rr in np.asarray(rr_array, dtype=float):
            self.update(rr)

    def expected(self):
        """Current expected level at each timescale."""
        return dict(self.state)

    def residuals(self, rr_window):
        """Deviation of a window from each expected level."""
        m = float(np.mean(rr_window))
        return {k: m - v for k, v in self.state.items()}

    @property
    def maturity(self):
        """How settled the personal baseline is.

        NOTE: these thresholds are PROVISIONAL. The minimum-window
        experiment was invalidated (Cosinor fits fell back to prefix
        means for 15/15 subjects), so no validated beat count exists
        yet. Re-run with EWMA before treating these as established.
        """
        if self.n_seen < 160:
            return "population"     # ~2 min, still essentially seeded
        if self.n_seen < 1600:
            return "converging"     # ~20 min
        return "personal"
