"""Wire format between clients and backend."""

from typing import Literal, Optional

from pydantic import BaseModel


class PPGBatch(BaseModel):
    """Mobile -> backend."""
    timestamp: float
    sample_rate: float = 64.0
    ppg: list[float]
    temperature: Optional[float] = None


class StressPrediction(BaseModel):
    """Backend -> Quest / website.

    `mode`, `level`/`level_low`/`level_high` and `label` are the
    authoritative decision. `probabilities` is supplementary — consumers
    must NOT re-derive a label from its argmax, which would bypass the
    confidence gate (docs/ARCHITECTURE.md §6).
    """
    # POSIX seconds as a float, matching the window's last beat
    timestamp: float
    mode: Literal["point", "band"]
    level: Optional[int] = None
    level_low: Optional[int] = None
    level_high: Optional[int] = None
    label: str
    confidence: float
    # the blended 4-vector before argmax, keyed by class name
    probabilities: dict[str, float]
    deviation: dict[str, float]
    baseline_maturity: str
    # bands only: whether the two merged levels are neighbours
    adjacent: Optional[bool] = None
