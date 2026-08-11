"""Circadian / time-of-day features.

Verbatim from notebooks/05_deployment/notebook-newmodel.ipynb cell 3.

Two different vectors, both load-bearing and NOT interchangeable:

- `circ7`  -> 7 dims, the MS-CGCA network's second input, projected into
              the cross-attention Query (docs/ARCHITECTURE.md §2).
- `circ_features` -> 5 dims, the tail of the flat XGBoost vector.

`ts` is a POSIX timestamp in seconds; only time-of-day is used.
"""

import numpy as np


def circ_features(ts):
    t, hour = ts % 86400, (ts % 86400) / 3600.0
    cort = 0.6 * np.exp(-0.5 * ((hour - 8) / 1.5) ** 2) + \
        0.3 * np.exp(-0.5 * ((hour - 15) / 1.5) ** 2)
    return np.array([
        np.sin(2 * np.pi * t / 86400),
        np.cos(2 * np.pi * t / 86400),
        np.sin(2 * np.pi * t / 5400),
        np.cos(2 * np.pi * t / 5400),
        cort,
    ])


def circ7(ts):
    t = ts % 86400
    hour = t / 3600.0
    return np.array([
        np.sin(2 * np.pi * t / 86400),
        np.cos(2 * np.pi * t / 86400),
        np.sin(2 * np.pi * t / 5400),
        np.cos(2 * np.pi * t / 5400),
        0.6 * np.exp(-0.5 * ((hour - 8) / 1.5) ** 2) +
        0.3 * np.exp(-0.5 * ((hour - 15) / 1.5) ** 2),
        np.sin(2 * np.pi * (hour - 23) / 24),
        np.cos(2 * np.pi * (hour - 23) / 24),
    ])
