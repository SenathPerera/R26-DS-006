from __future__ import annotations

from dataclasses import dataclass
import numpy as np

from .audio_state import AudioControlState


@dataclass
class SafetyFilterResult:
    safe_action: np.ndarray
    safe_state: AudioControlState
    safety_mode: str
    safety_violation: bool


class ConfidenceAwareSafetyFilter:
    def __init__(self, max_delta: float = 0.08) -> None:
        self.max_delta = max_delta

    def apply(
        self,
        proposed_action: np.ndarray,
        current_state: AudioControlState,
        baseline_state: AudioControlState,
        confidence: float,
        emergency_mute: bool = False,
    ) -> SafetyFilterResult:
        if emergency_mute:
            return SafetyFilterResult(
                safe_action=np.zeros_like(proposed_action, dtype=np.float32),
                safe_state=baseline_state.copy(),
                safety_mode="EmergencyMuted",
                safety_violation=True,
            )

        safety_mode = "Normal"
        action = np.clip(proposed_action.astype(np.float32), -self.max_delta, self.max_delta)

        if confidence < 0.25:
            safety_mode = "ConfidenceFreeze"
            action *= 0.0
        elif confidence < 0.45:
            safety_mode = "LowConfidenceDampened"
            action *= np.interp(confidence, [0.25, 0.45], [0.15, 0.50])

        next_vector = np.clip(current_state.as_vector() + action, 0.0, 1.0)
        safe_state = AudioControlState(*next_vector.tolist())
        total_mix = max(1e-6, safe_state.music_mix + safe_state.ambient_mix)
        safe_state.music_mix /= total_mix
        safe_state.ambient_mix /= total_mix

        baseline_delta = np.abs(safe_state.as_vector() - baseline_state.as_vector()).mean()
        if baseline_delta > 0.30:
            safety_mode = "BaselineRecovery"
            recover_vec = baseline_state.as_vector() + ((safe_state.as_vector() - baseline_state.as_vector()) * 0.5)
            safe_state = AudioControlState(*recover_vec.tolist())
            safe_state.music_mix = float(np.clip(safe_state.music_mix, 0.0, 1.0))
            safe_state.ambient_mix = float(np.clip(safe_state.ambient_mix, 0.0, 1.0))
            mix_total = max(1e-6, safe_state.music_mix + safe_state.ambient_mix)
            safe_state.music_mix /= mix_total
            safe_state.ambient_mix /= mix_total

        safe_action = safe_state.as_vector() - current_state.as_vector()
        violation = bool(np.any(np.abs(proposed_action) > self.max_delta * 1.5))
        return SafetyFilterResult(safe_action=safe_action.astype(np.float32), safe_state=safe_state, safety_mode=safety_mode, safety_violation=violation)
