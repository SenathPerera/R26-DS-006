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
    """Backend -> Quest / website."""
    timestamp: float
    mode: Literal["point", "band"]
    level: Optional[int] = None
    level_low: Optional[int] = None
    level_high: Optional[int] = None
    label: str
    confidence: float
    deviation: dict[str, float]
    baseline_maturity: str
    # bands only: whether the two merged levels are neighbours
    adjacent: Optional[bool] = None
