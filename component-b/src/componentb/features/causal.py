"""Causal replacements for whole-session transforms.

Verbatim from notebooks/05_deployment/notebook-newmodel.ipynb cell 3
("Causal Replacements for Live Deployment") — these produced the
reported results, so they are not to be rewritten or "optimised".

Every function here is strictly causal: output at index i depends only
on x[:i+1]. That is what lets the same code run over a batch in the
notebook and over a live stream in the server.
"""

import numpy as np

from componentb.config import POPULATION_RR_MS, ROLL_WINDOW, ZSCORE_HALFLIFE


def ewma_causal(x, halflife):
    a = 1 - np.exp(np.log(0.5) / max(halflife, 1))
    o = np.empty(len(x), dtype=float)
    state = float(POPULATION_RR_MS)
    for i in range(len(x)):
        state = a * x[i] + (1 - a) * state
        o[i] = state
    return o


def causal_zscore(x, halflife=ZSCORE_HALFLIFE):
    a = 1 - np.exp(np.log(0.5) / max(halflife, 1))
    mu = np.empty(len(x))
    sd = np.empty(len(x))
    m = float(x[0]) if len(x) else 0.0
    v = 1.0
    for i in range(len(x)):
        d = x[i] - m
        m = m + a * d
        v = (1 - a) * (v + a * d * d)
        mu[i] = m
        sd[i] = np.sqrt(max(v, 1e-8))
    return (x - mu) / (sd + 1e-8)


def roll_rmssd_causal(x, w=ROLL_WINDOW):
    o = np.zeros(len(x))
    for i in range(len(x)):
        seg = x[max(0, i - w + 1):i + 1]
        o[i] = np.sqrt(np.mean(np.diff(seg) ** 2)) if len(seg) > 1 else 0.0
    return o


def roll_sdnn_causal(x, w=ROLL_WINDOW):
    o = np.zeros(len(x))
    for i in range(len(x)):
        seg = x[max(0, i - w + 1):i + 1]
        o[i] = np.std(seg) if len(seg) > 1 else 0.0
    return o
