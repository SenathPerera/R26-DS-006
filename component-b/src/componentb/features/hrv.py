"""Feature extraction.

Reproduces notebooks/05_deployment/notebook-train-export-2way.ipynb cell 5
— the exact functions the shipped scaler and booster were fit against.
Feature ORDER matters and must not change.

Two details below look like bugs and are not. They are what the model was
trained on, so changing either one invalidates the artifacts:

  * the Welch resampling grid starts at 0, not at t[0]
  * the band integrals use unit spacing, not the frequency axis

Both are flagged in notebook-causalretrain.ipynb cell 9 as intentional
reproductions. An earlier port of this module "corrected" both and also
swapped the last two residual features, which silently fed the booster
two wrong columns — see tests/test_feature_parity.py.
"""

import numpy as np
from scipy.signal import welch

from componentb.config import XGB_FEATURE_ORDER

try:
    from scipy.integrate import trapezoid as TRAPZ
except ImportError:  # older scipy
    from scipy.integrate import trapz as TRAPZ


HRV_FEATURE_NAMES = [
    "mean_RR", "SDNN", "RMSSD", "pNN50", "CV_RR",
    "VLF", "LF", "HF", "LF/HF", "LF_nu", "SD1", "SD2", "SD1/SD2",
]
RESID_FEATURE_NAMES = [
    "res_mean", "res_SD", "res_maxabs", "res_slope", "res_msq",
]

# The names above are the contract the scaler was fitted under, so they are
# not documentation — they have to agree with the positions config.py
# declares. Checked at import because a mismatch here raises no error
# downstream: the vector still has 25 finite numbers, just in the wrong
# slots. This is exactly the drift that went unnoticed before.
assert HRV_FEATURE_NAMES + RESID_FEATURE_NAMES == XGB_FEATURE_ORDER[:18], (
    "feature names disagree with config.XGB_FEATURE_ORDER:\n"
    f"  here:      {HRV_FEATURE_NAMES + RESID_FEATURE_NAMES}\n"
    f"  config.py: {list(XGB_FEATURE_ORDER[:18])}"
)


def hrv_features(rr):
    """13 time- and frequency-domain HRV features from one window."""
    rr = np.asarray(rr, dtype=float)
    dd = np.diff(rr)
    nn = len(rr)

    rmssd = np.sqrt(np.mean(dd ** 2)) if nn > 1 else 0.0
    sdnn = np.std(rr)
    mean_rr = np.mean(rr)
    pnn50 = np.mean(np.abs(dd) > 50) * 100 if nn > 1 else 0.0
    cv = sdnn / (mean_rr + 1e-8)

    try:
        fs = 4.0
        t = np.cumsum(rr) / 1000.0
        # grid from 0, not t[0]: np.interp clamps the leading points to
        # rr[0]. Matches the training notebook; shifting the start moves
        # LF/HF by ~5%.
        ti = np.arange(0, t[-1], 1 / fs)
        ri = np.interp(ti, t, rr)
        # welch already applies detrend="constant", so the training code's
        # lack of an explicit mean subtraction costs nothing.
        f, pxx = welch(ri, fs=fs, nperseg=min(256, len(ri)))

        def band(lo, hi):
            m = (f >= lo) & (f < hi)
            # unit spacing, as trained. Passing f[m] here scales every band
            # by 1/df -- measured 26x to 64x, and df tracks window duration,
            # so the distortion varies with heart rate rather than cancelling.
            return TRAPZ(pxx[m]) if np.any(m) else 0.0

        vlf, lf, hf = band(0.003, 0.04), band(0.04, 0.15), band(0.15, 0.4)
    except Exception:
        vlf = lf = hf = 0.0

    tot = lf + hf + 1e-8
    sd1 = np.sqrt(0.5) * np.std(dd) if nn > 1 else 0.0
    sd2 = np.sqrt(max(2 * sdnn ** 2 - 0.5 * np.std(dd) ** 2, 0)) if nn > 1 else 0.0

    return np.array([
        mean_rr, sdnn, rmssd, pnn50, cv,
        vlf, lf, hf, lf / (hf + 1e-8), lf / tot,
        sd1, sd2, sd1 / (sd2 + 1e-8),
    ])


def resid_features(residual):
    """5 features describing deviation from the expected baseline.

    Order is res_mean, res_SD, res_maxabs, res_slope, res_msq. The last
    two were previously slope-at-index-4 with mean(abs(diff)) at index 3,
    which put a ~20 ms quantity into the slope column (+12 sigma under the
    scaler, against a training range of -7.4 to +2.8) and dropped res_msq
    entirely.
    """
    r = np.asarray(residual, dtype=float)
    return np.array([
        np.mean(r),
        np.std(r),
        np.max(np.abs(r)),
        np.polyfit(np.arange(len(r)), r, 1)[0],
        np.sum(r ** 2) / len(r),
    ])
