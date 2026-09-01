from __future__ import annotations

import numpy as np

from .audio_state import AudioControlState


class RuleBasedAdaptiveController:
    def __init__(self, max_delta: float = 0.08) -> None:
        self.max_delta = max_delta

    def get_action(self, stress: float, confidence: float, current_state: AudioControlState, baseline_state: AudioControlState) -> np.ndarray:
        target = baseline_state.copy()

        if confidence < 0.45:
            target = self._blend(current_state, baseline_state, 0.55)
        elif stress > 0.65:
            target.intensity = np.clip(baseline_state.intensity + 0.10, 0.0, 1.0)
            target.density = np.clip(baseline_state.density + 0.08, 0.0, 1.0)
            target.brightness = np.clip(baseline_state.brightness + 0.05, 0.0, 1.0)
            target.music_mix = np.clip(baseline_state.music_mix + 0.10, 0.0, 1.0)
            target.ambient_mix = np.clip(baseline_state.ambient_mix - 0.10, 0.0, 1.0)
        elif stress < 0.35:
            target.intensity = np.clip(baseline_state.intensity - 0.08, 0.0, 1.0)
            target.density = np.clip(baseline_state.density - 0.06, 0.0, 1.0)
            target.brightness = np.clip(baseline_state.brightness - 0.04, 0.0, 1.0)
            target.music_mix = np.clip(baseline_state.music_mix - 0.08, 0.0, 1.0)
            target.ambient_mix = np.clip(baseline_state.ambient_mix + 0.08, 0.0, 1.0)

        target = self._normalize_mix(target)
        delta = target.as_vector() - current_state.as_vector()
        return np.clip(delta, -self.max_delta, self.max_delta).astype(np.float32)

    @staticmethod
    def _blend(current: AudioControlState, target: AudioControlState, alpha: float) -> AudioControlState:
        current_vec = current.as_vector()
        target_vec = target.as_vector()
        blended = current_vec + ((target_vec - current_vec) * alpha)
        return AudioControlState(*blended.tolist())

    @staticmethod
    def _normalize_mix(state: AudioControlState) -> AudioControlState:
        total = max(1e-6, state.music_mix + state.ambient_mix)
        state.music_mix /= total
        state.ambient_mix /= total
        return state
