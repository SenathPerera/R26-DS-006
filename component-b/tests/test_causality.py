"""The baseline engine must never use future data."""

import numpy as np

from componentb.baseline.ewma import BaselineEngine


def test_baseline_is_causal():
    rng = np.random.default_rng(0)
    rr = rng.normal(800, 50, 1000)

    # run A: the real signal
    a = BaselineEngine()
    states_a = []
    for x in rr[:500]:
        a.update(x)
        states_a.append(a.expected()["medium"])

    # run B: identical first half, corrupted future
    corrupted = rr.copy()
    corrupted[500:] = 9999.0
    b = BaselineEngine()
    states_b = []
    for x in corrupted[:500]:
        b.update(x)
        states_b.append(b.expected()["medium"])

    assert np.allclose(states_a, states_b), "baseline used future data"


def test_cold_start_uses_population():
    e = BaselineEngine(population_level=780.0)
    assert e.expected()["fast"] == 780.0
    assert e.maturity == "population"
