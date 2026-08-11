"""The baseline engine and causal transforms must never use future data."""

import numpy as np
import pytest

from componentb.baseline.ewma import BaselineEngine
from componentb.features.causal import (
    causal_zscore, ewma_causal, roll_rmssd_causal, roll_sdnn_causal,
)


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


@pytest.mark.parametrize("fn", [
    causal_zscore,
    roll_rmssd_causal,
    roll_sdnn_causal,
    lambda x: ewma_causal(x, 300),
])
def test_sequence_channels_are_causal(fn):
    """Same corrupt-the-future check, applied to the channel transforms.

    These feed the MS-CGCA sequence input, so a leak here would leak
    into the model regardless of how careful the baseline engine is.
    """
    rng = np.random.default_rng(0)
    rr = rng.normal(800, 50, 1000)
    corrupted = rr.copy()
    corrupted[500:] = 9999.0

    assert np.allclose(fn(rr)[:500], fn(corrupted)[:500]), \
        f"{getattr(fn, '__name__', fn)} used future data"
