"""Feature extraction.

Verbatim from notebooks/01_pipeline/notebook-improvements.ipynb cell 6 —
the exact functions used to train the shipped model. Feature ORDER
matters and must not change — the scaler and model depend on it.
"""

import numpy as np
from scipy.signal import welch

try:
    from scipy.integrate import trapezoid as TRAPZ
except ImportError:  # older scipy
    from scipy.integrate import trapz as TRAPZ


HRV_FEATURE_NAMES = [
    "mean_RR", "SDNN", "RMSSD", "pNN50", "CV_RR",
    "VLF", "LF", "HF", "LF/HF", "LF_nu", "SD1", "SD2", "SD1/SD2",
]
RESID_FEATURE_NAMES = [
    "res_mean", "res_SD", "res_maxabs", "res_meandiff", "res_slope",
]


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
        ti = np.arange(t[0], t[-1], 1 / fs)
        ri = np.interp(ti, t, rr)
        f, pxx = welch(ri - np.mean(ri), fs=fs, nperseg=min(256, len(ri)))

        def band(lo, hi):
            m = (f >= lo) & (f < hi)
            return TRAPZ(pxx[m], f[m]) if np.any(m) else 0.0

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
    """5 features describing deviation from the expected baseline."""
    r = np.asarray(residual, dtype=float)
    dd = np.diff(r)
    return np.array([
        np.mean(r),
        np.std(r),
        np.max(np.abs(r)),
        np.mean(np.abs(dd)) if len(dd) > 0 else 0.0,
        np.polyfit(np.arange(len(r)), r, 1)[0],
    ])
