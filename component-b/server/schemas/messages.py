"""Wire format between clients and backend."""

from typing import Literal, Optional

from pydantic import BaseModel


class PPGBatch(BaseModel):
    """Mobile -> backend."""
    timestamp: float
    sample_rate: float = 64.0
    ppg: list[float]
    temperature: Optional[float] = None


class StressBlock(BaseModel):
    """The gated stress decision.

    `mode`, `level`/`level_low`/`level_high` and `label` are
    authoritative. `probabilities` and `continuous_score` are
    supplementary — a consumer that re-derives a label from either one
    bypasses the confidence gate (docs/ARCHITECTURE.md §6).
    """
    mode: Literal["point", "band"]
    # point mode
    level: Optional[int] = None
    # band mode: the two merged levels
    level_low: Optional[int] = None
    level_high: Optional[int] = None
    label: str
    confidence: float
    # whether the two merged levels are neighbours; always False for point
    adjacent: Optional[bool] = None
    # the blended 4-vector before argmax, keyed by class name
    probabilities: dict[str, float]
    # expected level under that distribution, sum(i * p_i). Derived.
    continuous_score: float


class StressPrediction(BaseModel):
    """Backend -> Quest / website."""
    # POSIX seconds. Equals windowEnd: labeling is endpoint, so the
    # prediction describes the window's last beat.
    timestamp: float
    # raw physiology in natural units, not the model's scaled inputs
    heartRate: float          # bpm, 60000 / mean RR of the window
    rmssd: float              # ms
    sdnn: float               # ms
    stress: StressBlock
    # fraction of the window's beats that arrived usable from the watch.
    # Heartbeat/RR data quality, NOT BLE or network signal strength.
    signalQuality: float
    windowStart: float
    windowEnd: float
