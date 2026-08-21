"""Live inference must reproduce the notebook that trained the model.

The most important test in the project. If it fails, live predictions
differ from the validated results and every reported number becomes
unverifiable in deployment.

Ground truth is `notebooks/05_deployment/notebook-train-export-2way.ipynb`.
It is checked at two levels, because neither alone is sufficient:

1. **Against the exported fixture** (`artifacts/fixtures/parity_fixture.npz`
   — 200 windows with the notebook's own `p_xgb` / `p_cnn`). These are the
   notebook's numbers, not a reimplementation, so nothing here can drift
   along with the code under test. This is what pins the artifacts.

2. **Streaming vs batch feature assembly**, driven from a synthetic beat
   segment. The fixture stores finished windows and carries no beat
   stream, so it cannot exercise the incremental FIFO at all — only this
   layer can. It compares against a batch reimplementation, which is
   exactly the weakness that let the midpoint/endpoint bug survive: both
   sides read the window midpoint and agreed with each other while
   disagreeing with the notebook. `test_window_timestamp_is_the_endpoint`
   therefore asserts the rule directly, against neither implementation.
"""

import json
from pathlib import Path

import numpy as np
import pytest

from componentb.config import (
    CIRCADIAN_DIM, CLASS_NAMES, EWMA_HALFLIVES, SEQ_CHANNELS, STEP_BEATS,
    WINDOW_BEATS, XGB_FEATURE_DIM,
)
from componentb.features.causal import (
    causal_zscore, ewma_causal, roll_rmssd_causal, roll_sdnn_causal,
)
from componentb.features.circadian import circ_features
from componentb.features.hrv import hrv_features, resid_features
from componentb.inference.stream import StreamingInference

ATOL = 1e-5
FIXTURE = Path(__file__).parent / "fixtures" / "rr_segment.json"
EXPORTED = (Path(__file__).resolve().parents[1]
            / "artifacts" / "fixtures" / "parity_fixture.npz")


# --------------------------------------------------------------------
# 1. against the exported fixture — the notebook's own numbers
# --------------------------------------------------------------------

def _load_exported():
    """The fixture plus the artifacts it describes, or None if absent."""
    try:
        import xgboost  # noqa: F401

        from componentb.models import loader
        if not EXPORTED.exists():
            return None
        return {
            "fx": np.load(EXPORTED),
            "model": loader.load_model(),
            "xgb": loader.load_xgb_model(),
            "scaler": loader.load_scaler(),
            "weights": loader.load_ensemble_weights(),
        }
    except Exception:
        return None


_EXPORT = _load_exported()
needs_export = pytest.mark.skipif(
    _EXPORT is None,
    reason=f"needs {EXPORTED.relative_to(EXPORTED.parents[2])} and xgboost — "
           "run notebook-train-export-2way.ipynb",
)


@needs_export
def test_exported_fixture_matches_the_shipped_config():
    """The fixture's shapes are the ones config.py claims are baked in."""
    fx = _EXPORT["fx"]
    assert fx["X_xgb"].shape[1] == XGB_FEATURE_DIM
    assert fx["X_seq"].shape[1:] == (WINDOW_BEATS, SEQ_CHANNELS)
    assert fx["X_circ"].shape[1] == CIRCADIAN_DIM
    assert fx["p_xgb"].shape[1] == fx["p_cnn"].shape[1] == len(CLASS_NAMES)


@needs_export
def test_scaler_and_xgb_reproduce_the_export():
    """`scaler.transform` -> `predict_proba` must match the notebook.

    Catches the silent failure mode: a scaler fitted on different
    statistics normalises fine and raises nothing, it just moves every
    prediction.
    """
    fx = _EXPORT["fx"]
    got = _EXPORT["xgb"].predict_proba(
        _EXPORT["scaler"].transform(fx["X_xgb"]))
    assert np.allclose(got, fx["p_xgb"], atol=ATOL)


@needs_export
def test_mscgca_reproduces_the_export():
    """The saved network returns the probabilities it was exported with."""
    fx = _EXPORT["fx"]
    got = _EXPORT["model"].predict(
        [fx["X_seq"], fx["X_circ"]], verbose=0)
    assert np.allclose(got, fx["p_cnn"], atol=ATOL)


@needs_export
def test_blend_matches_the_shipped_weights():
    """`_probabilities` blends exactly as the notebook's grid search did."""
    fx = _EXPORT["fx"]
    w_xgb, w_cnn = _EXPORT["weights"]
    assert np.isclose(w_xgb + w_cnn, 1.0)

    want = w_xgb * fx["p_xgb"] + w_cnn * fx["p_cnn"]
    assert np.allclose(want.sum(axis=1), 1.0, atol=ATOL)

    si = StreamingInference(model=_Const(fx["p_cnn"][0]),
                            xgb_model=_Const(fx["p_xgb"][0]),
                            scaler=None, weights=(w_xgb, w_cnn))
    _fill(si)
    assert np.allclose(si._probabilities(), want[0], atol=ATOL)


@needs_export
def test_end_to_end_windows_match_the_export():
    """Full path on the notebook's own windows: features in, blend out."""
    fx = _EXPORT["fx"]
    w_xgb, w_cnn = _EXPORT["weights"]
    flat = _EXPORT["scaler"].transform(fx["X_xgb"])

    got = (w_xgb * _EXPORT["xgb"].predict_proba(flat)
           + w_cnn * _EXPORT["model"].predict(
               [fx["X_seq"], fx["X_circ"]], verbose=0))
    want = w_xgb * fx["p_xgb"] + w_cnn * fx["p_cnn"]
    assert np.allclose(got, want, atol=ATOL), "shipped blend has drifted"


# --------------------------------------------------------------------
# 2. streaming vs batch feature assembly
# --------------------------------------------------------------------

def load_segment():
    d = json.loads(FIXTURE.read_text())
    rr = np.array(d["rr_ms"], dtype=float)
    temp = np.array(d["temp_c"], dtype=float)
    ts = d["t0"] + np.arange(len(rr)) * d["beat_dt_s"]
    return rr, temp, ts


def batch_channels(rr, temp):
    """notebook-train-export-2way.ipynb cell 3, whole-array form."""
    base = {k: ewma_causal(rr, hl) for k, hl in EWMA_HALFLIVES.items()}
    res_med = rr - base["medium"]
    temp_res = temp - ewma_causal(temp, EWMA_HALFLIVES["medium"])
    rn = causal_zscore(rr)
    seq = np.stack([
        rn,
        roll_rmssd_causal(rn),
        roll_sdnn_causal(rn),
        60000.0 / (rr + 1e-8),
        causal_zscore(res_med),
        causal_zscore(temp),
        causal_zscore(temp_res),
    ], axis=-1)
    return seq, base, res_med


def batch_xgb_vector(rr, base, res_med, s, e, ts):
    """The flat 25-dim vector for window [s, e), in the training order.

    `bi` is the ENDPOINT: cell 3 sets `li = e - 1` and `bi = min(li,
    len(ts)-1)`, so the time-of-day index follows the label.
    """
    bi = min(e - 1, len(ts) - 1)
    return np.concatenate([
        hrv_features(rr[s:e]),
        resid_features(res_med[s:e]),
        np.array([base["fast"][e - 1], base["slow"][e - 1]]),
        circ_features(ts[bi]),
    ])


class _Const:
    """A model that ignores its input, for testing the blend arithmetic."""

    def __init__(self, p):
        self.p = np.asarray(p, dtype=float)[None, ...]

    def predict(self, x, verbose=0):
        return self.p

    def predict_proba(self, x):
        return self.p


def _fill(si):
    """Drive an engine to exactly one full window."""
    rr, temp, ts = load_segment()
    for beat, t, stamp in zip(rr, temp, ts):
        if si.observe(beat, t, ts=stamp):
            return si
    raise AssertionError("segment too short to fill a window")


def stream_windows(rr, temp, ts, **kwargs):
    """Replay beats one at a time; yield (end_index, engine) per boundary.

    No model is configured: `observe` buffers without inference, so the
    feature comparisons exercise the real ingestion path with nothing
    stubbed at all.
    """
    si = StreamingInference(**kwargs)
    for i, (beat, t, stamp) in enumerate(zip(rr, temp, ts)):
        if si.observe(beat, t, ts=stamp):
            yield i + 1, si


def test_window_timestamp_is_the_endpoint():
    """The label sits at the window's last beat, and time-of-day follows it.

    Asserted against the raw timestamps rather than against either
    implementation, because the bug this guards was a batch helper and a
    streaming engine agreeing on the wrong index. Midpoint labeling was
    measured to inflate macro-F1 by +0.071 to +0.084
    (notebook-deployment-decision.ipynb).
    """
    rr, temp, ts = load_segment()
    checked = 0
    for e, si in stream_windows(rr, temp, ts):
        assert si._window_timestamp() == ts[e - 1]
        assert si._window_timestamp() != ts[e - WINDOW_BEATS // 2 - 1]
        checked += 1
    assert checked > 0


def test_observe_buffers_without_inference():
    """Beats can be ingested with no model loaded at all.

    Also pins the cadence: buffering is per beat, inference is per step.
    """
    rr, temp, ts = load_segment()
    si = StreamingInference()                    # no model, no weights
    boundaries = [i + 1 for i, (b, t, s) in enumerate(zip(rr, temp, ts))
                  if si.observe(b, t, ts=s)]

    assert boundaries == list(range(WINDOW_BEATS, len(rr) + 1, STEP_BEATS))
    assert si.window_full
    with pytest.raises(RuntimeError):            # still refuses to guess
        StreamingInference().predict()


def test_streaming_channels_match_batch():
    """The 7-channel model input is identical either way."""
    rr, temp, ts = load_segment()
    batch, _, _ = batch_channels(rr, temp)

    compared = 0
    for e, si in stream_windows(rr, temp, ts):
        got = si.channels.sequence()
        want = batch[e - WINDOW_BEATS:e]
        assert np.allclose(got, want, atol=ATOL), \
            f"channels diverged at beat {e}"
        compared += 1

    assert compared == (len(rr) - WINDOW_BEATS) // STEP_BEATS + 1


def test_streaming_xgb_vector_matches_batch():
    """The flat XGBoost vector is identical either way."""
    rr, temp, ts = load_segment()
    _, base, res_med = batch_channels(rr, temp)

    for e, si in stream_windows(rr, temp, ts):
        got = si._xgb_vector()
        want = batch_xgb_vector(rr, base, res_med, e - WINDOW_BEATS, e, ts)
        assert got.shape == (XGB_FEATURE_DIM,)
        assert np.allclose(got, want, atol=ATOL), \
            f"xgb vector diverged at beat {e}"


def test_blend_is_two_way():
    """Two members, two weights, no renormalisation.

    The personalised third member was rejected (+0.0066 F1, p = 0.625),
    and with it the cold-start special case: there is now one blend, used
    from the first window onwards.
    """
    p_cnn = [0.10, 0.20, 0.30, 0.40]
    p_xgb = [0.40, 0.30, 0.20, 0.10]
    w_xgb, w_cnn = 0.20, 0.80                    # the shipped pair

    si = StreamingInference(model=_Const(p_cnn), xgb_model=_Const(p_xgb),
                            scaler=None, weights=(w_xgb, w_cnn))
    _fill(si)
    probs = si._probabilities()

    assert np.allclose(
        probs, w_xgb * np.array(p_xgb) + w_cnn * np.array(p_cnn), atol=ATOL)
    assert np.isclose(probs.sum(), 1.0, atol=ATOL)


def test_output_carries_the_distribution():
    """`probabilities` ships with the decision, per ARCHITECTURE §6."""
    probs = np.array([0.04, 0.11, 0.81, 0.04])
    out = StreamingInference.format_output(probs)

    assert out["mode"] == "point" and out["label"] == "moderate"
    assert list(out["probabilities"]) == CLASS_NAMES
    assert np.isclose(sum(out["probabilities"].values()), 1.0, atol=1e-3)

    band = StreamingInference.format_output(np.array([0.05, 0.44, 0.46, 0.05]))
    assert band["mode"] == "band" and "level" not in band
    assert band["probabilities"]["moderate"] == 0.46


WESAD_S02 = Path("data/raw/S2/S2.pkl")


@pytest.mark.skipif(not WESAD_S02.exists(),
                    reason="WESAD S02 not present in data/raw/")
def test_wesad_s02_segment_matches_batch():
    """Same assertion against 100 real beats, when the dataset is local."""
    import pickle

    import neurokit2 as nk

    from componentb.signal.ppg import clean_rr

    with open(WESAD_S02, "rb") as f:
        data = pickle.load(f, encoding="latin1")

    ecg = data["signal"]["chest"]["ECG"].flatten()
    fs = 700
    _, info = nk.ecg_peaks(nk.ecg_clean(ecg, sampling_rate=fs),
                           sampling_rate=fs)
    peaks = info["ECG_R_Peaks"]
    rr = np.diff(peaks) * (1000.0 / fs)
    rr, _ = clean_rr(rr)
    rr = rr[:100]

    # the wrist TMP117 stream, resampled onto the same beats
    wrist_temp = data["signal"]["wrist"]["TEMP"].flatten()
    tap = np.interp(peaks / fs, np.arange(len(wrist_temp)) / 4.0, wrist_temp)
    temp = ((tap[:-1] + tap[1:]) / 2.0)[:100]
    ts = 1754985600.0 + np.cumsum(rr) / 1000.0

    batch, _, _ = batch_channels(rr, temp)
    for e, si in stream_windows(rr, temp, ts):
        assert np.allclose(si.channels.sequence(),
                           batch[e - WINDOW_BEATS:e], atol=ATOL), \
            f"channels diverged at beat {e} on real data"
