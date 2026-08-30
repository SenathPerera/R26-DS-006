from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List

import numpy as np


CONTROL_KEYS: List[str] = [
    "intensity",
    "density",
    "brightness",
    "tempo",
    "fade",
    "music_mix",
    "ambient_mix",
]


@dataclass
class AudioControlState:
    intensity: float
    density: float
    brightness: float
    tempo: float
    fade: float
    music_mix: float
    ambient_mix: float

    def as_vector(self) -> np.ndarray:
        return np.array(
            [
                self.intensity,
                self.density,
                self.brightness,
                self.tempo,
                self.fade,
                self.music_mix,
                self.ambient_mix,
            ],
            dtype=np.float32,
        )

    def to_dict(self) -> Dict[str, float]:
        return {
            "intensity": float(self.intensity),
            "density": float(self.density),
            "brightness": float(self.brightness),
            "tempo": float(self.tempo),
            "fade": float(self.fade),
            "music_mix": float(self.music_mix),
            "ambient_mix": float(self.ambient_mix),
        }

    @classmethod
    def from_dict(cls, values: Dict[str, float]) -> "AudioControlState":
        return cls(**{key: float(values[key]) for key in CONTROL_KEYS})

    def copy(self) -> "AudioControlState":
        return AudioControlState.from_dict(self.to_dict())


def normalize_mix(state: AudioControlState) -> AudioControlState:
    total = max(1e-6, state.music_mix + state.ambient_mix)
    state.music_mix /= total
    state.ambient_mix /= total
    return state
