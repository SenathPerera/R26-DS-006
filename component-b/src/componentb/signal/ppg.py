"""PPG -> RR intervals.

Ported directly from the validated Empatica notebook. Do not rewrite:
this exact code produced the reported cross-dataset results.
"""

import numpy as np
import neurokit2 as nk

from componentb.config import (
    PPG_SAMPLE_RATE, RR_MIN_MS, RR_MAX_MS, RR_JUMP_THRESHOLD,
)


def ppg_to_rr(ppg, fs=PPG_SAMPLE_RATE):
    """Detect beats in a raw PPG buffer and return RR intervals in ms.

    Returns (rr_ms, timestamps_s, peak_indices) or (None, None, None)
    if too few beats were detected to be useful.
    """
    clean = nk.ppg_clean(ppg, sampling_rate=fs)
    _, info = nk.ppg_peaks(clean, sampling_rate=fs)
    peaks = info["PPG_Peaks"]
    if len(peaks) < 10:
        return None, None, None
    rr = np.diff(peaks) * (1000.0 / fs)
    ts = (peaks[:-1] + peaks[1:]) / 2.0 / fs
    return rr, ts, peaks


def clean_rr(rr, ts=None):
    """Remove physiologically impossible values and artefact jumps."""
    rr = np.asarray(rr, dtype=float).copy()
    rr[(rr <= RR_MIN_MS) | (rr >= RR_MAX_MS)] = np.nan
    for i in range(1, len(rr)):
        if not np.isnan(rr[i - 1]) and not np.isnan(rr[i]):
            if abs(rr[i] - rr[i - 1]) / rr[i - 1] > RR_JUMP_THRESHOLD:
                rr[i] = np.nan
    m = np.isnan(rr)
    if m.any() and (~m).sum() >= 2:
        rr[m] = np.interp(np.where(m)[0], np.where(~m)[0], rr[~m])
    return rr, ts
