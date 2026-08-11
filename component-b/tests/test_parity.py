"""Streaming output must match the batch pipeline exactly.

This is the most important test in the project. If it fails, live
predictions differ from the validated notebook results and every
reported number becomes unverifiable in deployment.

The batch side re-implements notebook-newmodel.ipynb cell 3 directly
from `features/causal.py`; the streaming side runs the same beats
through the live FIFO (`features/channels.py`, driven by
`inference/stream.py`). Neither side imports the other's intermediates.

Note what this proves: streaming == batch. That the batch functions
themselves match the notebook is guaranteed by porting them verbatim
(see the module docstrings in `features/`), not by this test.
"""

import json
from pathlib import Path

import numpy as np
import pytest

from componentb.config import (
    EWMA_HALFLIVES, STEP_BEATS, WINDOW_BEATS, XGB_FEATURE_DIM,
)
from componentb.features.causal import (
    causal_zscore, ewma_causal, roll_rmssd_causal, roll_sdnn_causal,
)
from componentb.features.circadian import circ_features
from componentb.features.hrv import hrv_features, resid_features
from componentb.inference.stream import StreamingInference

ATOL = 1e-5
FIXTURE = Path(__file__).parent / "fixtures" / "rr_segment.json"
WESAD_S02 = Path("data/raw/S2/S2.pkl")


def load_segment():
    d = json.loads(FIXTURE.read_text())
    rr = np.array(d["rr_ms"], dtype=float)
    temp = np.array(d["temp_c"], dtype=float)
    ts = d["t0"] + np.arange(len(rr)) * d["beat_dt_s"]
    return rr, temp, ts


def batch_channels(rr, temp):
    """notebook-newmodel.ipynb cell 3, whole-array form."""
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
    """The flat 25-dim vector for window [s, e), in the training order."""
    mid = min(s + WINDOW_BEATS // 2, len(ts) - 1)
    return np.concatenate([
        hrv_features(rr[s:e]),
        resid_features(res_med[s:e]),
        np.array([base["fast"][e - 1], base["slow"][e - 1]]),
        circ_features(ts[mid]),
    ])


class _Const:
    """A model that ignores its input, for testing the blend arithmetic."""

    def __init__(self, p):
        self.p = np.array([p], dtype=float)

    def predict(self, x, verbose=0):
        return self.p

    def predict_proba(self, x):
        return self.p


def stream_windows(rr, temp, ts, **kwargs):
    """Replay beats one at a time; yield (end_index, engine, output).

    `push` always runs a prediction, so the feature-comparison tests
    still need *a* model. They get constant stubs by default: the
    features under test are computed before the model is consulted, so
    the stub cannot mask a divergence.
    """
    kwargs.setdefault("model", _Const([0.25, 0.25, 0.25, 0.25]))
    kwargs.setdefault("xgb_model", _Const([0.25, 0.25, 0.25, 0.25]))
    kwargs.setdefault("weights", (0.30, 0.35, 0.35))
    si = StreamingInference(**kwargs)
    for i, (beat, t, stamp) in enumerate(zip(rr, temp, ts)):
        out = si.push(beat, t, ts=stamp)
        if out is not None:
            yield i + 1, si, out


def test_streaming_channels_match_batch():
    """The 7-channel model input is identical either way."""
    rr, temp, ts = load_segment()
    batch, _, _ = batch_channels(rr, temp)

    compared = 0
    for e, si, _ in stream_windows(rr, temp, ts):
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

    for e, si, _ in stream_windows(rr, temp, ts):
        got = si._xgb_vector()
        want = batch_xgb_vector(rr, base, res_med, e - WINDOW_BEATS, e, ts)
        assert got.shape == (XGB_FEATURE_DIM,)
        assert np.allclose(got, want, atol=ATOL), \
            f"xgb vector diverged at beat {e}"


def test_cold_start_renormalises_to_half_and_half():
    """No personalised head -> 2-way ensemble at 0.50 / 0.50.

    The fine-tuned head is a per-user runtime artifact rather than a
    shipped file, so cold start is the normal path for a new wearer:
    (0.35, 0.35) renormalised over 0.70 is exactly 0.50 / 0.50.
    """
    rr, temp, ts = load_segment()
    p_cnn = [0.10, 0.20, 0.30, 0.40]
    p_xgb = [0.40, 0.30, 0.20, 0.10]

    for _, si, _ in stream_windows(
            rr, temp, ts,
            model=_Const(p_cnn), xgb_model=_Const(p_xgb),
            ft_model=None, scaler=None, weights=(0.30, 0.35, 0.35)):
        probs = si._probabilities()
        assert np.allclose(
            probs, 0.5 * np.array(p_cnn) + 0.5 * np.array(p_xgb), atol=ATOL)
        assert np.isclose(probs.sum(), 1.0, atol=ATOL)
        break


def test_personalised_head_uses_the_full_triple():
    """With a per-user head loaded, all three weights apply as shipped."""
    rr, temp, ts = load_segment()
    p_cnn = [0.10, 0.20, 0.30, 0.40]
    p_xgb = [0.40, 0.30, 0.20, 0.10]
    p_ft = [0.25, 0.25, 0.25, 0.25]
    w_ft, w_xgb, w_cnn = 0.30, 0.35, 0.35

    for _, si, _ in stream_windows(
            rr, temp, ts,
            model=_Const(p_cnn), xgb_model=_Const(p_xgb),
            ft_model=_Const(p_ft), scaler=None,
            weights=(w_ft, w_xgb, w_cnn)):
        expected = (w_xgb * np.array(p_xgb) + w_cnn * np.array(p_cnn)
                    + w_ft * np.array(p_ft))
        assert np.allclose(si._probabilities(), expected, atol=ATOL)
        break


# --- full-model comparison, only when artifacts and xgboost are present ---

def _artifacts_ready():
    try:
        import xgboost  # noqa: F401

        from componentb.models import loader
        loader.load_model()
        loader.load_xgb_model()
        loader.load_scaler()
        loader.load_ensemble_weights()
        return True
    except Exception:
        return False


@pytest.mark.skipif(not _artifacts_ready(),
                    reason="needs artifacts/ exports and xgboost installed")
def test_streaming_probabilities_match_batch():
    """batch_probs vs streaming_probs through the real ensemble."""
    from componentb.features.circadian import circ7
    from componentb.models import loader

    model = loader.load_model()
    xgb = loader.load_xgb_model()
    scaler = loader.load_scaler()
    weights = loader.load_ensemble_weights()
    w_ft, w_xgb, w_cnn = weights
    scale = w_xgb + w_cnn            # cold start: no per-user head

    rr, temp, ts = load_segment()
    batch, base, res_med = batch_channels(rr, temp)

    for e, si, _ in stream_windows(rr, temp, ts, model=model, xgb_model=xgb,
                                   ft_model=None, scaler=scaler,
                                   weights=weights):
        s = e - WINDOW_BEATS
        mid = min(s + WINDOW_BEATS // 2, len(ts) - 1)
        seq = batch[s:e].astype(np.float32)[None, ...]
        circ = circ7(ts[mid]).astype(np.float32)[None, ...]
        flat = scaler.transform(
            batch_xgb_vector(rr, base, res_med, s, e, ts)[None, ...])

        batch_probs = (
            w_xgb * np.asarray(xgb.predict_proba(flat))[0]
            + w_cnn * np.asarray(model.predict([seq, circ], verbose=0))[0]
        ) / scale

        assert np.allclose(si._probabilities(), batch_probs, atol=ATOL), \
            f"probabilities diverged at beat {e}"


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
    for e, si, _ in stream_windows(rr, temp, ts):
        assert np.allclose(si.channels.sequence(),
                           batch[e - WINDOW_BEATS:e], atol=ATOL), \
            f"channels diverged at beat {e} on real data"
