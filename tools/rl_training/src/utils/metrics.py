from __future__ import annotations

from typing import Dict, List
import numpy as np


def aggregate_metric_dicts(metric_rows: List[Dict[str, float]]) -> Dict[str, float]:
    keys = metric_rows[0].keys()
    aggregated: Dict[str, float] = {}
    for key in keys:
        values = np.array([row[key] for row in metric_rows], dtype=np.float64)
        aggregated[f"{key}_mean"] = float(values.mean())
        aggregated[f"{key}_std"] = float(values.std(ddof=0))
    return aggregated
