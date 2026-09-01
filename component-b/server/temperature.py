"""Temperature resolution for live Component B inference.

The trained pipeline expects a body-surface temperature channel. Until the
wearable temperature sensor is repaired, the server can provide a smooth,
bounded surrogate. The source is returned with every accepted-frame response
so synthetic research data is never mistaken for a physical measurement.
"""

from dataclasses import dataclass
import math
import os
from typing import Literal, Optional


TemperatureSource = Literal[
    "wearable",
    "wearable_cached",
    "synthetic_backend",
    "unavailable",
]

SYNTHETIC_BASE_C = 33.7
SYNTHETIC_MIN_C = 32.5
SYNTHETIC_MAX_C = 35.0
WEARABLE_CACHE_SECONDS = 60.0


def _environment_flag(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


@dataclass(frozen=True)
class TemperatureReading:
    value_c: Optional[float]
    source: TemperatureSource


class TemperatureResolver:
    """Resolve measured temperature or create a stable synthetic substitute."""

    def __init__(self, synthetic_enabled: Optional[bool] = None):
        self.synthetic_enabled = (
            _environment_flag("COMPONENT_B_SYNTHETIC_TEMPERATURE", True)
            if synthetic_enabled is None
            else synthetic_enabled
        )
        self._start_timestamp: Optional[float] = None
        self._last_measured_c: Optional[float] = None
        self._last_measured_timestamp: Optional[float] = None

    def resolve(
        self,
        measured_c: Optional[float],
        timestamp: float,
    ) -> TemperatureReading:
        if measured_c is not None and math.isfinite(measured_c):
            self._last_measured_c = float(measured_c)
            self._last_measured_timestamp = timestamp
            return TemperatureReading(round(float(measured_c), 3), "wearable")

        if (
            self._last_measured_c is not None
            and self._last_measured_timestamp is not None
            and 0 <= timestamp - self._last_measured_timestamp <= WEARABLE_CACHE_SECONDS
        ):
            return TemperatureReading(self._last_measured_c, "wearable_cached")

        if not self.synthetic_enabled:
            return TemperatureReading(None, "unavailable")

        if self._start_timestamp is None or timestamp < self._start_timestamp:
            self._start_timestamp = timestamp

        elapsed = max(0.0, timestamp - self._start_timestamp)
        # Two slow waves avoid random frame-to-frame jumps while staying in a
        # plausible wrist/body-surface range for the model's temperature input.
        value = (
            SYNTHETIC_BASE_C
            + 0.18 * math.sin(elapsed / 180.0)
            + 0.05 * math.sin(elapsed / 43.0)
        )
        bounded = min(SYNTHETIC_MAX_C, max(SYNTHETIC_MIN_C, value))
        return TemperatureReading(round(bounded, 3), "synthetic_backend")
