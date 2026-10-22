"""Runtime features vs the training notebook, from raw RR upward.

This is the gap that let a two-column feature mismatch ship. The existing
parity layers cannot see it:

  * the artifact layer loads the notebook's finished `X_xgb` and pushes it
    through the scaler and booster, so feature *generation* never runs;
  * the streaming-vs-batch layer calls the same `resid_features` on both
    sides, so both agreed while both disagreed with training.

So the oracle here is not a copy of the expected values and not a second
implementation in this repo. It is the training notebook's own source,
extracted from the .ipynb at test time and exec'd. If someone edits
`src/` to compute something else, this fails; if someone edits the
notebook, the model is retrained anyway and the fixture moves with it.
There is nowhere left for the two to agree on a wrong answer.

The defect this was written for: `resid_features` returned
`[mean, SD, maxabs, mean(abs(diff)), slope]` where training returned
`[mean, SD, maxabs, slope, msq]`, and `hrv_features` integrated the
spectral bands against the frequency axis instead of unit spacing.
Together that corrupted 6 of the 25 columns, flipped 13.5% of the
booster's own argmax, and moved 6.5% of the confidence-gate decisions
after the 0.15/0.85 blend.
"""

import json
from pathlib import Path

import numpy as np
import pytest

from componentb.config import XGB_FEATURE_ORDER
from componentb.features.hrv import (
    HRV_FEATURE_NAMES, RESID_FEATURE_NAMES, hrv_features, resid_features,
)

NOTEBOOK = (Path(__file__).resolve().parents[1] / "notebooks" / "05_deployment"
            / "notebook-train-export-2way.ipynb")

ATOL = 1e-9


def _notebook_feature_functions():
    """exec the notebook's own `hrv_features` / `resid_features`.

    Only the two `def` blocks are taken, not the whole cell — the cell
    also loads WESAD from a Kaggle path that does not exist here.
    """
    if not NOTEBOOK.exists():
        return None

    cells = json.loads(NOTEBOOK.read_text(encoding="utf-8"))["cells"]
    wanted = ("def hrv_features", "def resid_features")
    blocks = []
    for cell in cells:
        if cell["cell_type"] != "code":
            continue
        src = "".join(cell["source"])
        for marker in wanted:
            if marker not in src:
                continue
            tail = src[src.index(marker):]
            # the next top-level def ends this one
            nxt = tail.find("\ndef ", 1)
            blocks.append(tail[:nxt] if nxt != -1 else tail)

    if len(blocks) != len(wanted):
        return None

    from scipy.integrate import trapezoid
    from scipy.signal import welch
    ns = {"np": np, "welch": welch, "TRAPZ": trapezoid}
    exec("\n\n".join(blocks), ns)        # noqa: S102 - the notebook is the oracle
    return ns


_NB = _notebook_feature_functions()
needs_notebook = pytest.mark.skipif(
    _NB is None,
    reason=f"needs {NOTEBOOK.name} — the training notebook is the oracle for "
           "feature parity and cannot be substituted",
)


def rr_windows():
    """Deterministic 60-beat windows spanning a wide physiological range.

    Not WESAD: the point is to exercise the feature functions, and a fixed
    seed keeps the comparison reproducible without shipping subject data.
    """
    rng = np.random.default_rng(20260831)
    out = []
    for hr, rsa, jitter in ((52, 70, 6), (62, 55, 7), (75, 30, 10),
                            (94, 9, 3), (118, 5, 2)):
        mean_rr = 60000.0 / hr
        for phase in (0.0, 1.7, 3.4):
            beats = np.arange(60)
            rr = (mean_rr
                  + rsa * np.sin(2 * np.pi * 0.25 * beats * mean_rr / 1000
                                 + phase)
                  + rng.normal(0, jitter, 60))
            out.append(np.clip(rr, 320.0, 1900.0))
    return out


# --------------------------------------------------------------------
# the contract itself
# --------------------------------------------------------------------

def test_declared_names_match_the_shipped_feature_order():
    """hrv.py's names are the scaler's column contract, not a comment.

    `hrv.py` asserts this at import too; keeping it here means the failure
    is reported as a test rather than a collection error.
    """
    assert HRV_FEATURE_NAMES + RESID_FEATURE_NAMES == XGB_FEATURE_ORDER[:18]


def test_residual_order_is_slope_then_msq():
    """Pins the exact regression: slope at 16, msq at 17, no meandiff.

    Asserted on values rather than names, so renaming cannot satisfy it.
    A ramp has a known slope and a known mean square.
    """
    r = np.arange(60, dtype=float)          # slope exactly 1.0
    got = resid_features(r)

    assert RESID_FEATURE_NAMES[3:] == ["res_slope", "res_msq"]
    assert got[3] == pytest.approx(1.0, abs=1e-9)
    assert got[4] == pytest.approx(np.sum(r ** 2) / len(r), abs=1e-9)
    # mean(abs(diff)) of a unit ramp is also 1.0, so index 3 alone cannot
    # distinguish the two implementations -- index 4 is what pins it.
    assert got[4] != pytest.approx(np.mean(np.abs(np.diff(r))), abs=1e-6)


# --------------------------------------------------------------------
# against the notebook
# --------------------------------------------------------------------

@needs_notebook
@pytest.mark.parametrize("i", range(15))
def test_hrv_features_match_the_notebook(i):
    rr = rr_windows()[i]
    want = _NB["hrv_features"](rr)
    got = hrv_features(rr)

    assert got.shape == want.shape == (13,)
    bad = [f"{HRV_FEATURE_NAMES[k]}: notebook {want[k]!r} != runtime {got[k]!r}"
           for k in range(13)
           if not np.isclose(got[k], want[k], rtol=1e-9, atol=ATOL)]
    assert not bad, "\n".join(bad)


@needs_notebook
@pytest.mark.parametrize("i", range(15))
def test_resid_features_match_the_notebook(i):
    rr = rr_windows()[i]
    # residuals as stream.py forms them: RR minus the medium EWMA level
    from componentb.config import EWMA_HALFLIVES
    from componentb.features.causal import ewma_causal
    res = rr - ewma_causal(rr, EWMA_HALFLIVES["medium"])

    want = _NB["resid_features"](res)
    got = resid_features(res)

    assert got.shape == want.shape == (5,)
    bad = [f"{RESID_FEATURE_NAMES[k]}: notebook {want[k]!r} != runtime {got[k]!r}"
           for k in range(5)
           if not np.isclose(got[k], want[k], rtol=1e-9, atol=ATOL)]
    assert not bad, "\n".join(bad)


@needs_notebook
def test_spectral_bands_use_unit_spacing():
    """Guards the detail most likely to be 'fixed' by a future reader.

    Integrating against the frequency axis is defensible signal processing
    and wrong here: the model was fit on unit spacing. The error is a
    factor of 1/df, and df tracks window duration, so it does not cancel.
    """
    rr = rr_windows()[1]
    nb, rt = _NB["hrv_features"](rr), hrv_features(rr)

    lf_i, hf_i = HRV_FEATURE_NAMES.index("LF"), HRV_FEATURE_NAMES.index("HF")
    assert rt[lf_i] == pytest.approx(nb[lf_i], rel=1e-9)
    assert rt[hf_i] == pytest.approx(nb[hf_i], rel=1e-9)
    # band powers are O(1e3) under unit spacing and O(1e2) under df-scaling;
    # this would fail loudly rather than drift
    assert rt[lf_i] > 100.0


@needs_notebook
def test_full_25_vector_matches_the_notebook():
    """The whole assembled vector, not the two halves separately.

    Mirrors the notebook's cell 7 concatenation order exactly:
    hrv(13) + resid(5) + [fast, slow](2) + circ(5).
    """
    from componentb.config import EWMA_HALFLIVES, XGB_FEATURE_DIM
    from componentb.features.causal import ewma_causal
    from componentb.features.circadian import circ_features

    ts = 1787000000.0
    for rr in rr_windows():
        base = {k: ewma_causal(rr, hl) for k, hl in EWMA_HALFLIVES.items()}
        res = rr - base["medium"]
        tail = np.array([base["fast"][-1], base["slow"][-1]])
        circ = circ_features(ts)

        want = np.concatenate([_NB["hrv_features"](rr),
                               _NB["resid_features"](res), tail, circ])
        got = np.concatenate([hrv_features(rr),
                              resid_features(res), tail, circ])

        assert got.shape == (XGB_FEATURE_DIM,)
        bad = [f"idx {k} ({XGB_FEATURE_ORDER[k]}): "
               f"notebook {want[k]!r} != runtime {got[k]!r}"
               for k in range(XGB_FEATURE_DIM)
               if not np.isclose(got[k], want[k], rtol=1e-9, atol=ATOL)]
        assert not bad, "\n".join(bad)
